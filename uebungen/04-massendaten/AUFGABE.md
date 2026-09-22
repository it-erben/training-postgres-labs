# Übung 4: Massendaten, 35 Minuten

## Worum es geht

200000 Zeilen mit je einem `INSERT` bedeuten 200000 Roundtrips zum Server.
Bei einer Millisekunde Netzlatenz sind das über drei Minuten Wartezeit,
unabhängig davon, wie schnell der Server ist. PostgreSQL hat für solche
Ladevorgänge `COPY`: Die Zeilen fließen als ein Strom in einer Anweisung,
im Binärformat ohne Parsing je Zeile. Npgsql bietet dafür
`BeginBinaryImport`.

Brauchbar wird der Import durch zwei Eigenschaften. Eine fehlerhafte
Zeile in der Mitte darf keine halb geladene Tabelle hinterlassen. `COPY`
verwirft alle übertragenen Zeilen, wenn der Import nicht abgeschlossen
wird; du musst nichts zurückrollen, nur nicht abschließen. Und der Import
liest die Eingabe zeilenweise, während er schreibt. Wer die CSV erst
vollständig in den Speicher lädt, braucht
für eine 200-MB-Datei 200 MB Speicher und verliert die Zeit, in der Lesen
und Schreiben parallel laufen könnten.

## Was du vorfindest

```text
Rental/Database/Migrations/004_stock.sql        fertig: stock_movement und stock
Rental/Import/StockImport.cs                    ImportException und IStockImport fertig, StockImport ist Stub
Rental.Tests/Exercise04BulkData.cs
```

Die Zieltabelle:

```sql
CREATE TABLE rental.stock_movement (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    device_id integer NOT NULL REFERENCES rental.device (id),
    moved_at timestamptz NOT NULL,
    quantity integer NOT NULL CHECK (quantity > 0),
    remark text
);
```

Die Tests erzeugen die CSV selbst, 200000 Zeilen ohne Kopfzeile, Felder
durch Semikolon getrennt:

```text
2;2026-01-01T00:01:00.0000000Z;2;
3;2026-01-01T00:02:00.0000000Z;3;
...
1;2026-01-01T01:40:00.0000000Z;1;stocktake
```

`device_id` ist 1 bis 6, `moved_at` ist ISO 8601 in UTC, `quantity` ist 1
bis 5, `remark` ist leer oder `stocktake` (jede hundertste Zeile). Für den
Fehlertest steht in Zeile 150000 eine negative Menge.

Die Schnittstelle:

```csharp
Task<long> ImportAsync(Stream csv, CancellationToken ct = default);
Task UpdateStockAsync(IReadOnlyList<(int DeviceId, int Quantity)> stockLevels, CancellationToken ct = default);
```

`ImportException(int line, string reason)` trägt die Zeilennummer, beginnend
bei 1.

## Schritt für Schritt

### 1. Verbindung, Transaktion, Importer

Datei `Rental/Import/StockImport.cs`, Methode `ImportAsync`.

```csharp
await using var conn = await dataSource.OpenConnectionAsync(ct);
await using var tx = await conn.BeginTransactionAsync(ct);
await using var importer = await conn.BeginBinaryImportAsync(
    "COPY rental.stock_movement (device_id, moved_at, quantity, remark) FROM STDIN (FORMAT BINARY)", ct);
```

Die Spaltenliste im `COPY` legt fest, in welcher Reihenfolge du Werte
schreibst. `id` fehlt, weil sie der Server vergibt.

Die Transaktion ist hier nicht zwingend, weil `COPY` selbst atomar ist. Sie
macht den Ablauf aber lesbar und erlaubt später, weitere Anweisungen im
selben Commit zu ergänzen.

### 2. Zeilenweise lesen und schreiben

```csharp
using var reader = new StreamReader(csv);
long lines = 0;
string? line;
while ((line = await reader.ReadLineAsync(ct)) is not null)
{
    lines++;
    var fields = line.Split(';');
    // prüfen: vier Felder, int, DateTime, quantity > 0
    await importer.StartRowAsync(ct);
    await importer.WriteAsync(deviceId, NpgsqlDbType.Integer, ct);
    await importer.WriteAsync(movedAt, NpgsqlDbType.TimestampTz, ct);
    await importer.WriteAsync(quantity, NpgsqlDbType.Integer, ct);
    // remark: leer -> WriteNullAsync, sonst WriteAsync mit NpgsqlDbType.Text
}
```

`ReadLineAsync` liest aus dem Stream, sobald Daten da sind; der Importer
puffert die Zeilen und sendet sie in Blöcken. Lesen und Senden laufen so
verzahnt. `ReadToEndAsync` würde zuerst alles lesen und erst dann senden.

Für den Zeitpunkt:

```csharp
DateTime.TryParse(fields[1], CultureInfo.InvariantCulture,
    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var movedAt)
```

`AdjustToUniversal` liefert `Kind = Utc`; ohne diese Option entsteht
`Kind = Local` und `WriteAsync` mit `TimestampTz` wirft.

### 3. Fehler melden

Wenn ein Feld nicht passt oder `quantity <= 0` ist:

```csharp
throw new ImportException((int)lines, "quantity muss größer als 0 sein");
```

Die Ausnahme verlässt die Methode. `await using` disposed den Importer
ohne `CompleteAsync`; Npgsql bricht das `COPY` ab, und der Server verwirft
alle Zeilen. Danach disposed `await using` die Transaktion mit Rollback und
gibt die Verbindung an den Pool zurück. Du schreibst dafür keine Zeile
Aufräumcode.

Prüfe die Zeile, bevor du `StartRowAsync` aufrufst. Eine halb
geschriebene Zeile beim Abbruch ist zwar unschädlich, aber unnötig.

### 4. Abschließen

Nach der Schleife:

```csharp
await importer.CompleteAsync(ct);
await tx.CommitAsync(ct);
return lines;
```

`CompleteAsync` sendet das Ende des `COPY`; erst jetzt sind die Zeilen für
den Server verbindlich. Ohne diesen Aufruf ist ein Import, der ohne
Ausnahme durchläuft, trotzdem leer. Der Test
`Import_writes_200000_rows` fällt dann mit `0` statt `200000` auf.

## Wie der COPY-Test arbeitet

`Import_uses_COPY_and_streams_the_input` übergibt keinen gewöhnlichen
`MemoryStream`. Der Stream liefert die ersten 64 KB, danach blockiert er.
Parallel fragt der Test alle 50 ms `pg_stat_activity` ab, ob eine
Verbindung mit `application_name = 'rental'` eine Anweisung ausführt, die
mit `COPY` beginnt. Sobald das der Fall ist, gibt er den Stream frei, und
der Import läuft zu Ende.

Ein Import, der die CSV zuerst vollständig liest, wartet auf die Freigabe,
die nie kommt, weil das `COPY` noch nicht begonnen hat. Der Test bricht
nach 15 Sekunden mit einer Meldung ab, die genau das sagt. Ein Import mit
Einzel-INSERTs zeigt in `pg_stat_activity` `INSERT` statt `COPY` und
scheitert ebenso.

## Die Tests im Detail

| Test                                     | Was er prüft                                                               | Wenn er rot ist                                                                       |
| ---------------------------------------- | -------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `Import_writes_200000_rows`              | Rückgabewert, `count(*)`, 2000 Zeilen mit `stocktake`, kleinster Zeitpunkt | `CompleteAsync` fehlt, Bemerkung leer statt NULL nicht unterschieden, Zeitzone falsch |
| `Import_takes_at_most_five_seconds`      | Stoppuhr um den Aufruf                                                     | Einzel-INSERTs oder Batch; COPY braucht typisch unter einer Sekunde                   |
| `Import_uses_COPY_and_streams_the_input` | Blockierender Stream, `COPY` in `pg_stat_activity` innerhalb von 15 s      | Eingabe wird vollständig gelesen, oder kein COPY                                      |
| `Invalid_line_discards_the_whole_import` | `ImportException` mit `Line == 150000`, danach 0 Zeilen                    | Zeilen vor dem Fehler wurden bestätigt, oder Zeilennummer beginnt bei 0               |
| `Import_leaves_no_connection_open`       | Nach Erfolg und Fehler keine Verbindung `active` oder in einer Transaktion | Importer, Transaktion oder Verbindung nicht disposed                                  |

Die Zeitgrenze von fünf Sekunden ist für den Cluster des Kurses
kalibriert. Auf einem langsamen Netz kann sie knapp werden; die anderen
vier Tests sagen dann, ob der Import selbst richtig ist.

## Fallstricke

- Die Bonus-Grenze aus Übung 1 (`statement_timeout = 4s`) gilt für das
  `COPY` als eine Anweisung. Mit dem blockierenden Stream des COPY-Tests
  wartet das `COPY` auf Daten; PostgreSQL zählt Wartezeit auf den Client
  nicht als Anweisungslaufzeit, deshalb bleibt der Test unter der Grenze.
- `WriteAsync(string)` für eine leere Bemerkung speichert einen leeren
  Text, nicht NULL. Der erste Test zählt `remark = 'stocktake'`, das
  ist davon unabhängig, aber die Tabelle ist dann falsch befüllt.
- Ein `catch` um die Schleife, der die Ausnahme schluckt und
  `CompleteAsync` aufruft, bestätigt die Zeilen vor dem Fehler. Der
  Fehlertest findet dann 149999 Zeilen.

## Bonus

`Upsert_via_unnest_updates_existing_stock` (Trait `Stretch=true`):
`UpdateStockAsync` schreibt Bestände je Gerät in die Tabelle
`rental.stock` (`device_id` als Primärschlüssel, `quantity`). Vorhandene
Geräte werden überschrieben, neue eingefügt, alles in einem Roundtrip:

```sql
INSERT INTO rental.stock (device_id, quantity)
SELECT * FROM unnest($1::int[], $2::int[])
ON CONFLICT (device_id) DO UPDATE SET quantity = EXCLUDED.quantity
```

Die beiden Parameter sind `int[]`; Npgsql sendet .NET-Arrays als
PostgreSQL-Arrays. `unnest` mit mehreren Arrays liefert eine Zeile je
Position. Der Test schreibt drei Geräte, dann zwei davon mit neuen Werten
plus ein viertes, und prüft die Endwerte.

## Fertig, wenn

`dotnet test --filter-trait Exercise=04 --filter-not-trait Stretch=true` fünf
grüne Tests meldet.
