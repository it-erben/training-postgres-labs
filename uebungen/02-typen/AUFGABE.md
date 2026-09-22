# Übung 2: Typen und Schema, 35 Minuten

## Worum es geht

Eine Buchung hat einen Zeitraum, einen Gerätezustand bei Abholung und
freie Zusatzinformationen. PostgreSQL bietet dafür eigene Typen: einen
Bereichstyp `tstzrange`, einen Enum-Typ und `jsonb`. Npgsql bildet sie auf
.NET-Typen ab, wenn man ihm sagt, wie. Diese Übung baut die Tabelle, die
Zuordnung und die Zugriffe.

Dabei sollen zwei Dinge sichtbar werden. Der Server kann eine fachliche
Regel durchsetzen, die im Anwendungscode schwer ist: Ein
`EXCLUDE`-Constraint verhindert, dass zwei Buchungen desselben Geräts sich
zeitlich überlappen, auch wenn zwei Anwendungen gleichzeitig schreiben.
Daneben sind Zeitstempel die häufigste stille Fehlerquelle zwischen .NET
und PostgreSQL. Ein `DateTime` mit `Kind = Local` wird von Npgsql ohne
explizite Typangabe als `timestamp` ohne Zeitzone gesendet, und der Server
deutet den Wert dann in seiner Sitzungszeitzone um. Es gibt keinen Fehler,
nur eine Verschiebung um Stunden. Die Übung verlangt, dass so ein Wert
abgewiesen wird.

## Was du vorfindest

```text
Rental/Database/RentalDataSource.cs                 deine Lösung aus Übung 1; kennt noch keinen Enum und kein JSON
Rental/Database/Migrations/002_bookings.sql         leer bis auf Kommentare: die Migration schreibst du
Rental/Bookings/Booking.cs                          fertig: DeviceCondition, ExtraInfo, Booking, IBookingStore
Rental/Bookings/BookingStore.cs                     Stub: vier Methoden werfen NotImplementedException
Rental.Tests/Exercise02Types.cs                     die Tests dieser Übung
```

Die vorgegebenen Typen:

```csharp
public enum DeviceCondition { New, Used, Defective }

public sealed class ExtraInfo
{
    public required string PickupLocation { get; set; }
    public List<string> Notes { get; set; } = [];
}

public sealed record Booking(long Id, int DeviceId, int CustomerId, DateTime From, DateTime? To,
                             DeviceCondition ConditionAtPickup, ExtraInfo Extra);
```

`From` und `To` sind UTC-Zeitpunkte, `To` darf fehlen (offene Buchung).
Der Zeitraum ist halb offen: `From` gehört dazu, `To` nicht. So grenzen
`[10:00, 12:00)` und `[12:00, 14:00)` aneinander, ohne sich zu überlappen.

Der Migrator spielt alle `.sql`-Dateien im Verzeichnis `Migrations` in
Namensreihenfolge ein, jede in einer eigenen Transaktion, nachdem er das
Schema `rental` gelöscht und neu angelegt hat. Deine Migration läuft also
nach `001_devices.sql` und bei jedem Testlauf erneut.

## Schritt für Schritt

### 1. Die Migration schreiben

Datei `Rental/Database/Migrations/002_bookings.sql`. Sie braucht drei
Teile.

Den Enum-Typ mit Werten in Kleinbuchstaben. Npgsql übersetzt die
.NET-Namen `New`, `Used`, `Defective` standardmäßig in `new`,
`used`, `defective`:

```sql
CREATE TYPE rental.device_condition AS ENUM ('new', 'used', 'defective');
```

Die Tabelle mit Fremdschlüsseln auf `device` und `customer`, dem Zeitraum,
dem Zustand und dem JSON-Dokument:

```sql
CREATE TABLE rental.booking (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    device_id integer NOT NULL REFERENCES rental.device (id),
    customer_id integer NOT NULL REFERENCES rental.customer (id),
    time_range tstzrange NOT NULL,
    condition_at_pickup rental.device_condition NOT NULL,
    extra_info jsonb NOT NULL DEFAULT '{}'::jsonb
);
```

Die Tests lesen die Spalten unter genau diesen Namen. Ein Index auf
`customer_id` hilft Übung 3, ist hier aber nicht Gegenstand eines Tests.

Den Ausschluss-Constraint gegen Überlappung je Gerät. `EXCLUDE` prüft für
je zwei Zeilen eine Kombination von Operatoren; wenn alle zutreffen, wird
die zweite Zeile abgelehnt. Gebraucht werden Gleichheit auf `device_id`
und Überlappung `&&` auf `time_range`. Ein GiST-Index kann `&&` auf Bereichen,
aber `=` auf `integer` nur mit der Erweiterung `btree_gist`:

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE rental.booking ADD CONSTRAINT booking_no_overlap
    EXCLUDE USING gist (device_id WITH =, time_range WITH &&);
```

Der Constraint braucht einen Namen; Übung 3 prüft darauf. `btree_gist` ist
in PostgreSQL 13 und höher als vertrauenswürdig markiert und kann von der
Eigentümerin der Datenbank angelegt werden. Falls das auf dem Cluster
verweigert wird, beim Trainer melden.

Nach diesem Schritt läuft die Migration bei jedem Testlauf. Ein
Syntaxfehler zeigt sich als Fehler in `InitializeAsync` der Fixture bei
allen Tests der Klasse.

### 2. Die DataSource um Enum und JSON erweitern

Datei `Rental/Database/RentalDataSource.cs`, deine Lösung aus Übung 1. Zwei
Aufrufe am `NpgsqlDataSourceBuilder` vor `Build()`:

```csharp
builder.MapEnum<DeviceCondition>("rental.device_condition");
builder.ConfigureJsonOptions(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
builder.EnableDynamicJson();
```

`MapEnum` verbindet den .NET-Enum mit dem Servertyp, mit Schema. Ohne die
Zuordnung meldet Npgsql beim Schreiben
`Writing values of 'Rental.Bookings.DeviceCondition' is not supported ...`.

`EnableDynamicJson` erlaubt, beliebige Objekte als `jsonb` zu schreiben
und zu lesen; System.Text.Json serialisiert sie. Ohne die
Namensrichtlinie stehen die Schlüssel als `PickupLocation` im Dokument. Der Test
liest `extra_info ->> 'pickupLocation'` und erwartet Kleinschreibung, deshalb
`CamelCase`.

### 3. Buchung anlegen

Datei `Rental/Bookings/BookingStore.cs`. Es gibt zwei
`CreateAsync`-Methoden: die Instanzmethode aus der Schnittstelle und eine
statische Variante mit Verbindung und Transaktion. Die statische macht die
Arbeit, die Instanzmethode holt sich eine Verbindung und ruft sie auf.
Übung 3 wird dieselbe statische Methode innerhalb einer eigenen Transaktion
verwenden.

Der Zeitraum entsteht am besten auf dem Server aus zwei Zeitpunkten:

```sql
INSERT INTO rental.booking (device_id, customer_id, time_range, condition_at_pickup, extra_info)
VALUES ($1, $2, tstzrange($3, $4, '[)'), $5, $6)
RETURNING id
```

`tstzrange(from, to, '[)')` baut einen halb offenen Bereich; `to` darf
NULL sein. Für `$3` und `$4` gibst du den Typ explizit an:

```csharp
cmd.Parameters.Add(new NpgsqlParameter { Value = booking.From, NpgsqlDbType = NpgsqlDbType.TimestampTz });
cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)booking.To ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.TimestampTz });
```

Mit `NpgsqlDbType.TimestampTz` prüft Npgsql das `Kind` des `DateTime` und
wirft bei `Local` eine `ArgumentException`, bevor irgendetwas zum Server
geht. Ohne die Angabe leitet Npgsql den Typ aus dem `Kind` ab, sendet
`timestamp` und der Server castet in seiner Sitzungszeitzone. Der Test
`Local_timestamps_are_rejected` unterscheidet genau diese beiden
Wege.

Der Enum geht als `new NpgsqlParameter { Value = booking.ConditionAtPickup }`,
die Zusatzinfo mit `NpgsqlDbType = NpgsqlDbType.Jsonb`. `ExecuteScalarAsync`
liefert die ID aus `RETURNING`.

### 4. Buchung lesen

`LoadAsync(long id)` liest eine Zeile. Den Bereich zerlegst du auf dem
Server:

```sql
SELECT id, device_id, customer_id, lower(time_range), upper(time_range), condition_at_pickup, extra_info
FROM rental.booking WHERE id = $1
```

`lower` und `upper` liefern `timestamptz`; Npgsql gibt sie als `DateTime` mit
`Kind = Utc` zurück. `upper` ist NULL bei offener Buchung, also vorher
`reader.IsDBNull` prüfen. Den Enum liest
`reader.GetFieldValue<DeviceCondition>(5)`, das JSON-Dokument
`reader.GetFieldValue<ExtraInfo>(6)`.

### 5. Suche nach Abholort

`FindByPickupLocationAsync(string pickupLocation)` nutzt den Enthält-Operator
von `jsonb`. Der Parameter ist ein Dokument, das im gespeicherten Dokument
enthalten sein muss:

```csharp
await using var cmd = dataSource.CreateCommand(SelectSql + " WHERE extra_info @> $1 ORDER BY id");
cmd.Parameters.Add(new NpgsqlParameter { Value = new { pickupLocation }, NpgsqlDbType = NpgsqlDbType.Jsonb });
```

Das anonyme Objekt wird zu `{"pickupLocation": "Halle 7"}`. Ein `string` als
Wert ginge auch, dann als Text `"""{"pickupLocation": "..."}"""`; die
Typangabe `Jsonb` bleibt nötig, sonst sendet Npgsql `text`.

## Die Tests im Detail

| Test                                                  | Was er tut                                                                                      | Wenn er rot ist                                                                                |
| ----------------------------------------------------- | ----------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `Booking_stores_time_range_as_tstzrange`              | Legt eine Buchung an, liest `pg_typeof`, `lower`, `upper`, `lower_inc`, `upper_inc`             | Spaltentyp falsch oder Grenzen `[]` statt `[)`                                                 |
| `Timestamps_come_back_as_utc`                         | Legt an, lädt, prüft `From.Kind == Utc` und die Werte                                           | `lower(time_range)` fehlt oder Spalte ist `timestamp` ohne Zeitzone                            |
| `Local_timestamps_are_rejected`                       | Legt mit `Kind = Local` an, erwartet `ArgumentException` und keine Zeile mit Abholort `Halle 9` | `NpgsqlDbType.TimestampTz` fehlt am Parameter; der Wert wurde still gespeichert                |
| `Overlapping_booking_of_same_device_fails_with_23P01` | Zwei überlappende Buchungen für Gerät 4, erwartet `PostgresException` mit `23P01` und Namen     | `EXCLUDE` fehlt, ohne Namen, oder Migration nicht eingespielt                                  |
| `Adjacent_bookings_are_allowed`                       | `[08:00,10:00)` und `[10:00,12:00)` für Gerät 5                                                 | Bereich ist `[]` statt `[)`                                                                    |
| `Overlap_of_different_devices_is_allowed`             | Gleicher Zeitraum für Gerät 1 und 2                                                             | `device_id WITH =` fehlt im Constraint                                                         |
| `Extra_info_is_stored_as_jsonb_in_camelCase`          | Liest `pg_typeof`, `extra_info ->> 'pickupLocation'` und die Länge von `notes`                  | Namensrichtlinie fehlt (`PickupLocation` statt `pickupLocation`) oder Spalte ist `json`/`text` |
| `Find_by_extra_info_uses_jsonb_parameter`             | Zwei Buchungen mit `Halle 7`, Suche liefert beide mit Hinweisen                                 | `@>` fehlt, Parameter ist `text`, oder `Notes` werden nicht gelesen                            |
| `Device_condition_is_an_enum_on_both_sides`           | Prüft `pg_type.typtype = 'e'`, speichert `Defective`, liest `defective` und den Enum zurück     | Typ fehlt, `MapEnum` fehlt, oder die Labels sind nicht kleingeschrieben                        |

## Fallstricke

- Die Migration läuft in einer Transaktion. Ein Fehler in Zeile 20
  verwirft auch die Zeilen davor. Die Fehlermeldung steht in der Ausgabe
  der Fixture, nicht im einzelnen Test.
- `MapEnum` ohne Schema (`"device_condition"`) sucht den Typ in `public` und
  findet ihn nicht.
- `DateTime.Now` hat `Kind = Local`, `DateTime.UtcNow` hat `Kind = Utc`.
  In den Tests sind alle Zeitpunkte mit `DateTimeKind.Utc` konstruiert.
- `reader.GetDateTime()` auf einer `timestamptz`-Spalte liefert `Kind = Utc`;
  auf `timestamp` ohne Zeitzone liefert es `Kind = Unspecified`.

## Bonus

`Time_range_without_end_means_open_booking` (Trait `Stretch=true`): Eine
Buchung mit `To = null` sperrt das Gerät für alle späteren Zeiträume. Der
Test legt sie an, erwartet `23P01` für eine Buchung 20 Tage später und
findet die offene Buchung über die Suche nach Abholort mit `To == null`.
Wenn Schritt 3 und 4 NULL korrekt behandeln, ist dieser Test bereits grün.

## Fertig, wenn

`dotnet test --filter-trait Exercise=02 --filter-not-trait Stretch=true` neun
grüne Tests meldet und `dotnet test --filter-trait Exercise=01` weiterhin
grün ist. Die Tests aus Übung 1 laufen mit deiner erweiterten DataSource.
