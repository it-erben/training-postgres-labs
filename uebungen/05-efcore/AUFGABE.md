# Übung 5: EF Core, 40 Minuten

## Ziel

EF Core arbeitet über dem bestehenden Schema. Das erzeugte SQL bleibt
sichtbar und knapp: keine N+1-Abfragen, keine Änderungsverfolgung für
Übersichten, Mengenänderungen ohne Laden, `xmin` als Versionsmerkmal.

## Ausgangslage

`Verleih/Modell/Entitaeten.cs` ist vorgegeben. `VerleihContext` hat ein
leeres `OnModelCreating` und eine leere `Konfiguriere`-Methode;
`Kundenuebersicht` enthält Stubs. Die Migration `005_bonus.sql` ergänzt
`kunde.bonuspunkte`. Die Tests bauen den Kontext mit einem eigenen
`DbCommandInterceptor`, der jede Anweisung zählt.

## Aufgabe

1. `VerleihContext.Konfiguriere`: Enum-Zuordnung für den Provider. Die
   `NpgsqlDataSource` aus Übung 2 kennt den Enum bereits; der Provider muss
   ihn zusätzlich kennen.
2. `OnModelCreating`: Tabellen und Spalten des Schemas `verleih` abbilden,
   `zusatzinfo` als JSON-Dokument mit camelCase-Schlüsseln, `xmin` als
   Versionsspalte von `kunde`.
3. `Kundenuebersicht.LadeAsync`: alle Kunden mit Buchungen ohne Tracking in
   höchstens zwei Anweisungen. `BonusGutschriftAsync`: ein `UPDATE` für alle
   Kunden mit mindestens einer Buchung, ohne die Kunden zu laden.

## Anforderungen

| Test                                                   | Prüft                                                        |
| ------------------------------------------------------ | ------------------------------------------------------------ |
| `Modell_passt_zum_bestehenden_Schema`                  | Alle drei Entitäten laden, Zeitraum, Enum und JSON stimmen   |
| `Zusatzinfo_ist_als_jsonb_abgebildet`                  | `ToQueryString` enthält `->>`                                |
| `Kundenuebersicht_braucht_hoechstens_zwei_Anweisungen` | 50 Kunden mit je 3 Buchungen, Interceptor zählt              |
| `Uebersicht_laedt_ohne_Tracking`                       | `ChangeTracker.Entries()` leer                               |
| `Bonusgutschrift_nutzt_ExecuteUpdate`                  | Genau eine Anweisung, `UPDATE`                               |
| `Gleichzeitige_Aenderung_wird_ueber_xmin_erkannt`      | `DbUpdateConcurrencyException`, `xmin` im `WHERE`            |

## Hinweise

- [Npgsql EF Core: Enums](https://www.npgsql.org/efcore/mapping/enum.html)
- [Npgsql EF Core: Concurrency Tokens](https://www.npgsql.org/efcore/modeling/concurrency.html)
- [EF Core: JSON Columns](https://learn.microsoft.com/ef/core/what-is-new/ef-core-7.0/whatsnew#json-columns)
- [EF Core: ExecuteUpdate](https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete)
- `HasJsonPropertyName` gleicht die JSON-Schlüssel an das an, was Npgsql in
  Übung 2 mit camelCase geschrieben hat.
- `Property<uint>("xmin").IsRowVersion()` nutzt die Systemspalte ohne
  eigene Spalte in der Tabelle.

## Bonus

`Buchungsdauer_wird_auf_dem_Server_berechnet`: Die Differenz von
`UpperBound` und `LowerBound` des Zeitraums wird in SQL berechnet; das SQL
lädt keine `zusatzinfo`.

## Fertig, wenn

`dotnet test --filter-trait Uebung=05 --filter-not-trait Stretch=ja` sechs
grüne Tests meldet.
