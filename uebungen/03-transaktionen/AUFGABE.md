# Übung 3: Transaktionen, 40 Minuten

## Worum es geht

Eine fachliche Regel lautet: Ein Kunde darf höchstens drei Buchungen
haben. Der naheliegende Code zählt die Buchungen, vergleicht mit drei und
fügt ein. Wenn zwei Anfragen desselben Kunden gleichzeitig laufen, zählen
beide zwei, beide fügen ein, und am Ende sind es vier. Kein Constraint
verhindert das, weil die Regel über mehrere Zeilen geht.

PostgreSQL löst das mit der Isolationsstufe `SERIALIZABLE`. Der Server
erkennt, dass zwei Transaktionen voneinander abhängen, und bricht eine
davon mit SQLSTATE `40001` ab. Die Anwendung muss diese Transaktion dann
vollständig wiederholen, einschließlich der Zählung. Beim zweiten Anlauf
sieht sie drei Buchungen und lehnt fachlich ab.

In dieser Übung baust du den Buchungsdienst mit dieser Transaktion, der
Wiederholung mit begrenzter Versuchszahl und der Unterscheidung zwischen
wiederholbaren Fehlern und Constraint-Verletzungen. Die Tests erzeugen
Konflikte gezielt über einen Hook, den nur sie setzen; so brauchen sie
keine Wartezeiten und sind deterministisch.

## Was du vorfindest

```text
Rental/Bookings/Exceptions.cs         fertig: BookingRejectedException und BookingConflictException
Rental/Bookings/BookingService.cs     Stub mit Konstanten, Eigenschaften und zwei Methoden
Rental/Bookings/BookingStore.cs       deine Lösung aus Übung 2 mit der statischen CreateAsync
Rental.Tests/Exercise03Transactions.cs
```

Der Stub:

```csharp
public sealed class BookingService(NpgsqlDataSource dataSource)
{
    public const int MaxBookingsPerCustomer = 3;
    public int MaxAttempts { get; set; } = 3;
    public Func<NpgsqlConnection, CancellationToken, Task>? BeforeCheck { get; set; }
    public bool UseAdvisoryLock { get; set; }

    public Task<long> BookAsync(Booking booking, CancellationToken ct = default) => throw ...;
    public static bool IsRetryable(PostgresException e) => throw ...;
}
```

Die beiden Ausnahmen:

- `BookingRejectedException(string reason)`: Die fachliche Regel lehnt ab. Kein
  Fehler des Servers.
- `BookingConflictException(string constraintName, Exception inner)`: Ein
  Constraint hat die Buchung verhindert, etwa die Überlappung aus Übung 2.
  Trägt den Constraint-Namen.

`BeforeCheck` ist der Hook der Tests. Er wird innerhalb der Transaktion
aufgerufen, nach `BEGIN` und vor der Zählung, mit der Verbindung der
Transaktion. Die Tests nutzen ihn, um die Isolationsstufe zu lesen, um
einen Konflikt zu erzwingen oder um zwei Transaktionen an derselben Stelle
warten zu lassen. In der Anwendung bleibt er `null`.

## Schritt für Schritt

### 1. Ein einzelner Versuch

Schreibe zuerst eine private Methode `AttemptAsync(Booking, CancellationToken)`,
die genau einen Durchlauf macht, ohne Wiederholung:

1. Verbindung aus der DataSource öffnen (`await using`).
2. Transaktion beginnen: `conn.BeginTransactionAsync(IsolationLevel.Serializable, ct)`,
   ebenfalls `await using`. Ein `await using` auf der Transaktion rollt bei
   jeder Ausnahme zurück, ohne dass du `Rollback` aufrufen musst.
3. Wenn `BeforeCheck` gesetzt ist: `await BeforeCheck(conn, ct)`.
4. Zählen: `SELECT count(*) FROM rental.booking WHERE customer_id = $1` als
   `NpgsqlCommand(sql, conn, tx)`. Das Ergebnis ist `long`.
5. Wenn die Zahl `>= MaxBookingsPerCustomer` ist: `BookingRejectedException`
   werfen. Die Transaktion wird durch `await using` zurückgerollt.
6. Sonst `BookingStore.CreateAsync(conn, tx, booking, ct)` aufrufen,
   `tx.CommitAsync(ct)`, ID zurückgeben.

Das Kommando bekommt die Transaktion als dritten Konstruktorparameter.
Ohne sie läuft die Anweisung zwar auf derselben Verbindung, Npgsql weist
aber darauf hin, dass eine Transaktion offen ist.

### 2. Wiederholbare Fehler erkennen

`IsRetryable` liefert `true` für `PostgresErrorCodes.SerializationFailure`
(`40001`) und `PostgresErrorCodes.DeadlockDetected` (`40P01`). Beide sagen:
Die Transaktion war fachlich in Ordnung, aber sie hat mit einer anderen
kollidiert. Ein zweiter Anlauf hat gute Aussichten.

Alle anderen SQLSTATEs sind nicht wiederholbar. Eine Überlappung (`23P01`)
wird beim zweiten Mal genauso überlappen.

### 3. Die Schleife

`BookAsync` ruft `AttemptAsync` bis zu `MaxAttempts` mal:

```csharp
for (var attempt = 1; ; attempt++)
{
    try
    {
        return await AttemptAsync(booking, ct);
    }
    catch (PostgresException e) when (IsRetryable(e) && attempt < MaxAttempts)
    {
        // nächste Runde
    }
    catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.ExclusionViolation
                                        or PostgresErrorCodes.UniqueViolation
                                        or PostgresErrorCodes.CheckViolation
                                        or PostgresErrorCodes.ForeignKeyViolation)
    {
        throw new BookingConflictException(e.ConstraintName ?? e.SqlState, e);
    }
}
```

Beim dritten wiederholbaren Fehler greift der erste `catch` nicht mehr
(`attempt < MaxAttempts` ist falsch), und die `PostgresException` verlässt
die Methode. Der Test `Conflict_is_attempted_at_most_three_times` prüft
genau diesen Ablauf.

`BookingRejectedException` und `OperationCanceledException` laufen durch beide
`catch`-Blöcke hindurch und werden nicht wiederholt.

### 4. Prüfen, was der Hook sieht

Der Hook bekommt die Verbindung der laufenden Transaktion. Der Test
`Booking_runs_in_a_serializable_transaction` führt darin
`SELECT current_setting('transaction_isolation')` aus und erwartet
`serializable`. Wenn du `BeginTransactionAsync()` ohne Isolationsstufe
aufrufst, kommt `read committed` zurück.

## Wie die Tests Konflikte erzeugen

Der Hook erlaubt drei Dinge, die ohne ihn nur mit Wartezeiten und Glück
gingen:

**Ein erzwungener Konflikt.** Der Hook führt auf der Verbindung aus:

```sql
DO $$ BEGIN RAISE EXCEPTION 'Testkonflikt' USING ERRCODE = '40001'; END $$
```

Der Server meldet `40001`, als hätte er einen echten Konflikt erkannt. Die
Transaktion ist danach abgebrochen; dein Code muss sie aufgeben und neu
beginnen. Der Test zählt, wie oft der Hook aufgerufen wurde: dreimal bei
dauerhaftem Konflikt, zweimal bei einmaligem.

**Zwei echte Transaktionen an derselben Stelle.** Der Test
`Two_concurrent_third_bookings_let_exactly_one_through` startet zwei
Tasks für denselben Kunden, der bereits zwei Buchungen hat. Der Hook lässt
den ersten Task warten, bis der zweite auch angekommen ist. Dann zählen
beide zwei Buchungen, beide fügen ein, und beim zweiten `COMMIT` meldet
der Server `40001`. Die Wiederholung zählt drei und wirft
`BookingRejectedException`. Am Ende hat der Kunde genau drei Buchungen, ein Task
war erfolgreich, einer wurde abgelehnt. Der Test prüft alle drei Aussagen.

**Ein Constraint statt eines Konflikts.** Der Test
`Constraint_violation_is_not_retried` bucht dasselbe Gerät zweimal
zur selben Zeit. Der zweite Versuch scheitert mit `23P01`. Erwartet wird
`BookingConflictException` mit `ConstraintName == "booking_no_overlap"`
und dass der Hook nur zweimal lief, einmal je Buchung; ein Wiederholen
wäre ein dritter Aufruf.

## Die Tests im Detail

| Test                                                    | Was er prüft                                                                          | Wenn er rot ist                                                                       |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `Booking_runs_in_a_serializable_transaction`            | `transaction_isolation` im Hook ist `serializable`                                    | Isolationsstufe nicht gesetzt oder Hook außerhalb der Transaktion aufgerufen          |
| `Fourth_booking_is_rejected_by_business_rule`           | Drei Buchungen gelingen, die vierte wirft `BookingRejectedException`, es bleiben drei | Zählung fehlt oder vergleicht mit `>` statt `>=`                                      |
| `Two_concurrent_third_bookings_let_exactly_one_through` | Genau ein Erfolg, genau eine Ablehnung, drei Buchungen                                | Keine Wiederholung nach `40001`, oder Zählung außerhalb der Transaktion               |
| `Conflict_is_attempted_at_most_three_times`             | Dauerhafter Konflikt: drei Hook-Aufrufe, dann `PostgresException` mit `40001`         | Endlosschleife (Test hängt) oder Abbruch nach dem ersten Fehler                       |
| `Retry_repeats_the_business_check`                      | Einmaliger Konflikt: zwei Hook-Aufrufe, Buchung gespeichert                           | Nur die letzte Anweisung wiederholt statt der ganzen Transaktion                      |
| `Constraint_violation_is_not_retried`                   | `BookingConflictException` mit Constraint-Namen, kein zusätzlicher Versuch            | `23P01` wird wie `40001` behandelt oder nicht in `BookingConflictException` übersetzt |
| `No_open_transaction_remains_after_an_error`            | Nach dem Fehler keine Verbindung `idle in transaction` oder `(aborted)`               | Transaktion oder Verbindung nicht disposed                                            |

## Fallstricke

- Nach einem Fehler innerhalb der Transaktion ist sie abgebrochen. Jede
  weitere Anweisung meldet `25P02`. Deshalb wird nicht die Anweisung
  wiederholt, sondern die ganze Transaktion mit neuer Verbindung.
- `40001` kann beim `COMMIT` auftreten, nicht nur bei einer Anweisung.
  `tx.CommitAsync` gehört in den `try`-Block.
- Der Hook darf werfen. Eine Ausnahme aus dem Hook, die keine
  `PostgresException` ist, muss unverändert nach außen gelangen.
- Der Standard von `BeginTransactionAsync()` ist `ReadCommitted`. Damit
  sieht der zweite Task im Nebenläufigkeitstest keinen Konflikt und es
  entstehen vier Buchungen.

## Bonus

Beide Bonus-Tests tragen den Trait `Stretch=true`.

`Cancellation_token_ends_the_server_query`: Der Hook führt
`SELECT pg_sleep(10)` mit dem übergebenen Token aus; der Test bricht nach
300 ms ab. Erwartet wird eine `OperationCanceledException`, genau ein
Hook-Aufruf (kein Wiederholen nach Abbruch), keine aktive
Anwendungsverbindung mehr und keine gespeicherte Buchung. Dafür muss das
`CancellationToken` an jede Anweisung und an `BeginTransactionAsync` und
`CommitAsync` weitergereicht werden. Npgsql sendet beim Abbruch eine
Cancel-Anfrage an den Server, der die Abfrage mit `57014` beendet.

`Advisory_lock_per_customer_avoids_the_conflict`: Mit `UseAdvisoryLock = true`
hält die Transaktion vor dem Hook `SELECT pg_advisory_xact_lock($1)` mit der
Kundennummer. Zwei Buchungen desselben Kunden laufen dann nacheinander
statt in Konflikt. Der Test prüft im Hook, dass `pg_locks` für die eigene
PID genau eine gewährte Sperre vom Typ `advisory` zeigt. Die Sperre endet
mit der Transaktion.

## Fertig, wenn

`dotnet test --filter-trait Exercise=03 --filter-not-trait Stretch=true` sieben
grüne Tests meldet. Der Nebenläufigkeitstest ist der wichtigste; wenn nur
er rot ist, fehlt meist die Isolationsstufe oder die Wiederholung.
