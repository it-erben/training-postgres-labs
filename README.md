# PostgreSQL mit .NET: Übungsserie

Sechs aufeinander aufbauende Übungen an einem Verleihdienst mit Geräten,
Kunden und Buchungen. Jede Übung hat einen Startpunkt als Git-Tag, eine
Aufgabenbeschreibung unter `uebungen/`, Tests, die zu Beginn rot sind, und
eine Musterlösung als weiteres Git-Tag. Wer die Tests einer Übung grün hat,
ist mit ihr fertig.

## Voraussetzungen

| Baustein                              | Version                       |
| ------------------------------------- | ----------------------------- |
| .NET SDK                              | 10                            |
| Npgsql                                | 10.0.3                        |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3                        |
| xunit.v3                              | 4.0.1                         |
| PostgreSQL                            | 17 oder 18, eigene Datenbank  |

Die Rolle ist Eigentümerin ihrer Datenbank und kein Superuser. Die Erweiterung
`btree_gist` muss in der Datenbank anlegbar sein; in PostgreSQL 13 und höher
ist sie als vertrauenswürdig markiert und von der Eigentümerin installierbar.

## Einrichtung

Die Verbindungszeichenfolge kommt aus der Umgebungsvariablen
`VERLEIH_CONNECTION`. Zugangsdaten stehen nie im Repository.

```sh
export VERLEIH_CONNECTION='Host=<cluster>-rw;Port=5432;Database=<datenbank>;Username=<rolle>;Password=<passwort>'
dotnet test --filter-trait Uebung=00
```

Bei CloudNativePG liegen Host, Datenbank, Rolle und Passwort im Secret der
Datenbank; Übung 0 beschreibt den Weg. Für Übung 6 kann zusätzlich
`VERLEIH_CONNECTION_RO` auf den Dienst `<cluster>-ro` zeigen; ohne diese
Variable wird der betroffene Test übersprungen.

## Ablauf

```sh
git checkout -b meine-uebung-01 uebung-01-start
dotnet test --filter-trait Uebung=01
```

Die Tests einer Übung sind zu Beginn rot und nennen den fehlenden Baustein.
Bonus-Tests tragen den Trait `Stretch=ja` und zählen nicht zur Abnahme:

```sh
dotnet test --filter-trait Uebung=03 --filter-not-trait Stretch=ja
```

Vor der nächsten Übung die eigene Arbeit committen. Der Startpunkt der
nächsten Übung enthält die Musterlösung der vorigen:

```sh
git add -A && git commit -m "Uebung 1"
git checkout -b meine-uebung-02 uebung-02-start
```

Wer eine Übung nicht abschließt, wechselt auf die Musterlösung und arbeitet
von dort weiter:

```sh
git checkout uebung-01-loesung
```

`AUFGABEN.md` enthält die Übersicht mit Zeiten. Jede Übung hat unter
`uebungen/` ihre Aufgabe mit den Testnamen als Anforderungen.

## Aufbau

```text
Verleih/                 Anwendung: Datenbank, Geräte, Buchungen, Import, Modell, Betrieb
Verleih.Tests/           Ein Testprojekt, Tests je Übung über den Trait Uebung
uebungen/                Aufgabenbeschreibungen je Übung
```

Die Anwendung ist eine Klassenbibliothek ohne Web-API; die Tests sind der
einzige Aufrufer. Die Tests bauen das Schema `verleih` vor jeder Testklasse
neu auf und lesen den Serverzustand über `pg_stat_activity`,
`pg_prepared_statements` und die Systemkataloge.
