# Übung 6: Betrieb, 30 Minuten

## Worum es geht

Die letzte Übung verlässt die einzelne Anweisung und sieht auf die
Anwendung im Betrieb. Wie erfahren andere Teile des Systems, dass ein
Gerät zurückgegeben wurde, ohne die Tabelle abzufragen? Wie sieht
die Anwendung ihre eigenen Serververbindungen, um ein Problem zu erkennen,
bevor der DBA anruft? Und wie liest sie von einem Replikat, wenn der
Cluster eines hat?

Für die erste Frage bietet PostgreSQL `LISTEN` und `NOTIFY`. Ein `NOTIFY`
innerhalb einer Transaktion wird erst beim `COMMIT` zugestellt, bei einem
`ROLLBACK` gar nicht. Empfänger brauchen eine eigene Verbindung, die nicht
durch den Pool wandert, und die Anwendung muss damit rechnen, dass diese
Verbindung abbricht.

## Was du vorfindest

```text
Rental/Database/Migrations/006_return.sql          fertig: booking.returned_at timestamptz
Rental/Operations/Return.cs                        Stubs: ReturnService, ReturnListener, ConnectionDiagnostics, ReadDataSource
Rental.Tests/Exercise06Operations.cs
```

In `Return.cs` sind vier Klassen mit Signaturen vorgegeben:

```csharp
public static class NotifyChannels { public const string Return = "rental_return"; }

public sealed class ReturnService(NpgsqlDataSource dataSource)
{
    public Func<Task>? BeforeCommit { get; set; }                    // Hook der Tests
    public Task ReturnAsync(long bookingId, CancellationToken ct = default);
}

public sealed class ReturnListener(string connectionString)
{
    public const string AppName = "rental_listener";
    public string ConnectionString { get; }                           // im Stub unverändert = connectionString
    public Task RunAsync(Func<long, Task> onReturn, CancellationToken ct);
}

public sealed record ConnectionInfo(int Pid, string AppName, string State, string? WaitEventType);

public sealed class ConnectionDiagnostics(NpgsqlDataSource dataSource)
{
    public Task<IReadOnlyList<ConnectionInfo>> OwnConnectionsAsync(CancellationToken ct = default);
}

public static class ReadDataSource
{
    public const string AppName = "rental_reader";
    public static NpgsqlDataSource Create(string connectionWithAllHosts);
}
```

Die Tests haben einen eigenen Lauscher: eine Verbindung mit `LISTEN` auf
dem Kanal, unabhängig von deinem Melder. Damit prüfen sie, wann eine
Benachrichtigung ankommt.

## Schritt für Schritt

### 1. Rückgabe mit Benachrichtigung

`ReturnService.ReturnAsync`. Eine Transaktion mit drei Schritten:

```csharp
await using var conn = await dataSource.OpenConnectionAsync(ct);
await using var tx = await conn.BeginTransactionAsync(ct);

// 1. UPDATE rental.booking SET returned_at = now()
//    WHERE id = $1 AND returned_at IS NULL
//    Trifft es keine Zeile: InvalidOperationException (unbekannt oder schon zurückgegeben)
// 2. SELECT pg_notify($1, $2)  mit NotifyChannels.Return und bookingId.ToString()
// 3. if (BeforeCommit is not null) await BeforeCommit();
await tx.CommitAsync(ct);
```

`pg_notify` ist die Funktionsform von `NOTIFY` und nimmt Kanal und Payload
als Parameter, während `NOTIFY kanal, 'text'` beides fest im SQL-Text
erwartet. Der Payload ist Text, deshalb die Buchungsnummer als
Zeichenkette.

Der Hook `BeforeCommit` läuft nach dem `NOTIFY` und vor dem `COMMIT`. Der
Test `Notification_does_not_arrive_before_commit` wartet im Hook 500 ms
auf seinen Lauscher und erwartet, dass nichts ankommt. Der Test
`Rolled_back_return_sends_nothing` wirft im Hook eine Ausnahme;
`await using` rollt zurück, und der Lauscher darf nichts empfangen.

### 2. Der Melder

`ReturnListener`. Zuerst die Verbindungszeichenfolge im Konstruktor
aufbereiten; der Test liest sie über `ConnectionString`:

```csharp
public string ConnectionString { get; } = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = AppName,
    KeepAlive = 10,
    Pooling = false,
}.ConnectionString;
```

`Pooling = false`: Der Melder hält seine Verbindung selbst; sie darf nicht
nach einer Anweisung an einen Pool zurückgehen. `KeepAlive = 10` sendet
alle zehn Sekunden ein Lebenszeichen, damit Firewalls und Load Balancer
die ruhende Verbindung nicht schließen.

Dann `RunAsync`: eine Schleife, die bis zum Abbruch läuft und nach einem
Verbindungsverlust neu beginnt:

```csharp
while (!ct.IsCancellationRequested)
{
    try
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        conn.Notification += (_, e) => { if (long.TryParse(e.Payload, out var id)) _ = onReturn(id); };
        // LISTEN rental_return ausführen
        while (!ct.IsCancellationRequested)
        {
            await conn.WaitAsync(ct);   // blockiert, bis eine Benachrichtigung eintrifft
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
    catch (NpgsqlException) { await Task.Delay(200, ct); }   // Verbindung verloren, neu aufbauen
}
```

`WaitAsync` wartet auf dem Socket. Trifft eine Benachrichtigung ein, löst
Npgsql das Ereignis `Notification` aus und `WaitAsync` kehrt zurück.
`LISTEN` gilt je Verbindung; nach einem Neuaufbau muss es erneut
ausgeführt werden, deshalb steht es innerhalb der äußeren Schleife.

### 3. Diagnose

`ConnectionDiagnostics.OwnConnectionsAsync` liest `pg_stat_activity` für die
Application Names der Anwendung: `rental`, `rental_listener` und
`rental_reader`. Die Namen der Tests (`rental_test`, `rental_test_listener`)
dürfen nicht dabei sein; ein `LIKE 'rental%'` wäre zu weit.

```sql
SELECT pid, application_name, state, wait_event_type
FROM pg_stat_activity
WHERE datname = current_database() AND application_name = ANY($1)
ORDER BY application_name, pid
```

`$1` ist ein `string[]`. `state` und `wait_event_type` können NULL sein.

### 4. Lesen vom Replikat

`ReadDataSource.Create` bekommt eine Verbindungszeichenfolge, deren `Host`
mehrere Namen durch Komma trennt, etwa `Host=cluster-ro,cluster-rw`.
Npgsql probiert die Hosts in dieser Reihenfolge und kann nach der Rolle
des Servers filtern:

```csharp
var settings = new NpgsqlConnectionStringBuilder(connectionWithAllHosts)
{
    ApplicationName = AppName,
    TargetSessionAttributes = "prefer-standby",
};
return NpgsqlDataSource.Create(settings.ConnectionString);
```

`prefer-standby` nimmt ein Replikat, wenn eines antwortet, sonst den
Primärserver. Der Test liest `pg_is_in_recovery()` über diese DataSource
und erwartet `true`. Er läuft nur, wenn `RENTAL_CONNECTION_RO` gesetzt
ist; sonst wird er übersprungen und sagt das in der Ausgabe. Ob der
Cluster ein Replikat hat, sagt der Trainer.

## Die Tests im Detail

| Test                                                  | Was er prüft                                                                                      | Wenn er rot ist                                                             |
| ----------------------------------------------------- | ------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `Return_sends_notification_after_commit`              | Lauscher empfängt die Buchungsnummer, `returned_at` ist gesetzt                                   | `pg_notify` fehlt oder anderer Kanal, Payload nicht die ID                  |
| `Notification_does_not_arrive_before_commit`          | Im Hook nach 500 ms nichts empfangen, nach dem Commit doch                                        | `NOTIFY` außerhalb der Transaktion, oder Hook nach dem Commit aufgerufen    |
| `Rolled_back_return_sends_nothing`                    | Hook wirft; kein Empfang, `returned_at` bleibt NULL, keine offene Transaktion                     | Commit vor dem Hook, oder Ausnahme abgefangen                               |
| `Listener_uses_a_dedicated_connection_with_keepalive` | `KeepAlive > 0`, Application Name, genau eine Verbindung `rental_listener`, Empfang innerhalb 5 s | Zeichenfolge unverändert, Pooling an (zwei Verbindungen), `WaitAsync` fehlt |
| `Diagnostics_lists_own_connections_with_state`        | Melder `idle`, Anwendung `active` (die Diagnoseabfrage selbst), keine Testverbindungen            | `LIKE 'rental%'` liefert auch `rental_test`                                 |
| `Read_queries_prefer_the_replica`                     | `pg_is_in_recovery()` ist `true` über die Lesequelle                                              | `TargetSessionAttributes` fehlt; ohne `RENTAL_CONNECTION_RO` übersprungen   |

## Fallstricke

- `NOTIFY` liefert nichts nach, was vor dem `LISTEN` gesendet wurde. Der
  Melder muss laufen, bevor eine Rückgabe erfolgt; die Tests warten darauf,
  dass seine Verbindung in `pg_stat_activity` erscheint.
- Der Payload ist auf 8000 Bytes begrenzt. Die Benachrichtigung weckt den
  Empfänger nur; den Zustand liest er aus der Tabelle.
- Ohne `Pooling = false` öffnet der Melder eine Poolverbindung; nach dem
  `LISTEN` wäre sie beim nächsten Kommando eine andere. Der Test findet
  dann zwei Verbindungen oder keine mit `LISTEN`.
- `WaitAsync` ohne Token wartet unbegrenzt; der Melder wäre dann nicht
  abbrechbar und der Test hinge beim Aufräumen.

## Bonus

`Listener_reconnects_after_connection_loss` (Trait `Stretch=true`): Der
Test beendet die Melderverbindung mit `pg_terminate_backend`, wartet auf
eine neue Verbindung mit anderer PID und löst dann eine Rückgabe aus, die
ankommen muss. Die Schleife aus Schritt 2 mit dem `catch (NpgsqlException)`
erfüllt das; ohne die äußere Schleife bleibt der Melder nach dem Abbruch
tot.

## Fertig, wenn

`dotnet test --filter-trait Exercise=06 --filter-not-trait Stretch=true` fünf
grüne Tests meldet und einer übersprungen ist, falls kein Replikat
konfiguriert ist. Damit ist die Serie abgeschlossen; `dotnet test` ohne
Filter zeigt alle 50 Tests.
