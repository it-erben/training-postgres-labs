# Übung 1: Verbindungen und Anweisungen, 35 Minuten

## Ziel

Die Anwendung hat genau einen Einstiegspunkt in die Datenbank, eine
`NpgsqlDataSource`. Sie ist am Server erkennbar, hält den Pool klein,
bereitet wiederholte Anweisungen vor und übergibt Werte nur als Parameter.

## Ausgangslage

`Verleih/Datenbank/Datenquelle.cs` und `Verleih/Geraete/GeraeteKatalog.cs`
enthalten Stubs. Die Migration `001_geraete.sql` legt Geräte und Kunden an;
die Tests spielen sie vor jeder Testklasse ein.

## Aufgabe

1. `Datenquelle.Erzeuge` baut aus der übergebenen Verbindungszeichenfolge
   eine `NpgsqlDataSource` mit `Application Name = verleih`, höchstens vier
   Poolverbindungen, einem `Timeout` von höchstens fünf Sekunden und
   eingeschaltetem Auto-Prepare.
2. `GeraeteKatalog` liefert Geräte nach Kategorie und nach Namensbestandteil.
   Beide Suchen übergeben den Suchbegriff als Parameter.

## Anforderungen

| Test                                                       | Prüft                                                                    |
| ---------------------------------------------------------- | ------------------------------------------------------------------------ |
| `Suche_nach_Kategorie_liefert_erwartete_Geraete`           | Zwei Geräte der Kategorie Bohrer, nach Name sortiert                     |
| `Suche_verwendet_Parameter_statt_Verkettung`               | Suchbegriff `'; DROP TABLE verleih.geraet; --` liefert nichts, Tabelle besteht |
| `DataSource_traegt_Application_Name_verleih`               | `pg_stat_activity.application_name`                                      |
| `Verbindung_wird_nach_Gebrauch_an_den_Pool_zurueckgegeben` | Nach dem Aufruf keine Verbindung `active` oder `idle in transaction`     |
| `Pool_haelt_hoechstens_vier_Serververbindungen`            | Vier gehaltene Verbindungen, die fünfte scheitert innerhalb des Timeouts |
| `DataSource_bereitet_wiederholte_Anweisungen_automatisch_vor` | `pg_prepared_statements` nach der dritten Ausführung nicht leer       |

## Hinweise

- [Npgsql: Basic Usage](https://www.npgsql.org/doc/basic-usage.html)
- [Npgsql: Connection String Parameters](https://www.npgsql.org/doc/connection-string-parameters.html)
- [Npgsql: Prepared Statements](https://www.npgsql.org/doc/prepare.html)
- `NpgsqlConnectionStringBuilder` setzt Einstellungen ohne Zeichenkettenbastelei.
- `ILIKE '%' || $1 || '%'` sucht mit Parameter nach einem Namensbestandteil.

## Bonus

`Statement_Timeout_gilt_je_Verbindung`: Keine Anweisung der Anwendung läuft
länger als vier Sekunden. Der Parameter `Options` der Verbindungszeichenfolge
setzt Serverparameter je Verbindung. Achtung: Die späteren Übungen laufen mit
dieser Grenze; der Import in Übung 4 muss sie unterschreiten.

## Fertig, wenn

`dotnet test --filter-trait Uebung=01 --filter-not-trait Stretch=ja` sechs
grüne Tests meldet.
