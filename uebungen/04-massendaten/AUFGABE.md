# Übung 4: Massendaten, 35 Minuten

## Ziel

200000 Bestandsbewegungen aus einer CSV landen in einem Roundtrip über COPY
in der Datenbank. Eine fehlerhafte Zeile verwirft den gesamten Import.

## Ausgangslage

`Verleih/Import/Bestandsimport.cs` enthält Stubs. Die Migration
`004_bestand.sql` legt `verleih.bestandsbewegung` und `verleih.bestand` an.
Die Tests erzeugen die CSV selbst: `geraet_id;zeitpunkt;menge;bemerkung`
ohne Kopfzeile, Zeitpunkt in ISO 8601 mit `Z`.

## Aufgabe

`Bestandsimport.ImportiereAsync` liest die CSV zeilenweise, prüft jede Zeile
(vier Felder, Zahl, Datum, `menge > 0`) und schreibt über
`BeginBinaryImportAsync` in `verleih.bestandsbewegung`. Ein Fehler löst
`ImportFehler` mit der Zeilennummer aus und lässt den Server alles verwerfen.

## Anforderungen

| Test                                             | Prüft                                                                         |
| ------------------------------------------------ | ----------------------------------------------------------------------------- |
| `Import_schreibt_200000_Zeilen`                  | Zeilenzahl, Bemerkungen, kleinster Zeitpunkt                                  |
| `Import_braucht_hoechstens_fuenf_Sekunden`       | Stoppuhr; Einzel-INSERTs erreichen das Budget nicht                           |
| `Import_nutzt_COPY_und_streamt_die_Eingabe`      | Der Stream blockiert nach 64 KB, bis `pg_stat_activity` ein `COPY` der Anwendung zeigt; wer die CSV zuerst ganz liest, wartet ewig |
| `Fehlerhafte_Zeile_verwirft_den_gesamten_Import` | Zeile 150000 mit negativer Menge: 0 Zeilen, `ImportFehler.Zeile = 150000`     |
| `Import_laesst_keine_Verbindung_offen`           | Keine Verbindung `active` oder `idle in transaction` nach Erfolg und Fehler   |

## Hinweise

- [Npgsql: COPY](https://www.npgsql.org/doc/copy.html)
- `StreamReader.ReadLineAsync` liest zeilenweise; `ReadToEndAsync` liest alles
  und scheitert am dritten Test.
- Ohne `CompleteAsync` verwirft der Server die übertragenen Zeilen. Eine
  Ausnahme vor `CompleteAsync` ist deshalb genau das gewünschte Verhalten.
- `DateTimeStyles.AdjustToUniversal | AssumeUniversal` liefert `Kind = Utc`.
- Die Grenze aus dem Bonus von Übung 1 gilt weiter: vier Sekunden je Anweisung.

## Bonus

`Upsert_per_unnest_aktualisiert_bestehende_Bestaende`:
`AktualisiereBestandAsync` schreibt Bestände je Gerät in einem Roundtrip mit
`INSERT ... SELECT FROM unnest($1, $2) ON CONFLICT DO UPDATE`.

## Fertig, wenn

`dotnet test --filter-trait Uebung=04 --filter-not-trait Stretch=ja` fünf
grüne Tests meldet.
