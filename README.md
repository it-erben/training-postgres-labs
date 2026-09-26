# PostgreSQL-Übungen

Zwei Übungsserien für den PostgreSQL-Kurs: eine SQL-Serie im pgAdmin Query
Tool gegen ein Ticketsystem, eine .NET-Serie mit einem Verleihdienst aus
Geräten, Kunden und Buchungen.

## SQL-Serie

Fünfzehn Übungen an einem Ticketsystem mit Agents, Tickets und Kommentaren,
eine je Kursmodul. Arbeitsweise, Aufbau der Übungen und die vollständige
Übersicht stehen in [sql/README.md](sql/README.md).

## .NET-Serie

Sechs aufeinander aufbauende Übungen an einem Verleihdienst mit Geräten,
Kunden und Buchungen. Jede Übung hat einen Startpunkt als Git-Tag, eine
Aufgabenbeschreibung unter `dotnet/`, Tests, die zu Beginn rot sind, und
eine Musterlösung als weiteres Git-Tag. Wer die Tests einer Übung grün hat,
ist mit ihr fertig.

### Voraussetzungen

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

### Einrichtung

Die Verbindungszeichenfolge kommt aus der Umgebungsvariablen
`RENTAL_CONNECTION`. Zugangsdaten stehen nie im Repository.

```sh
export RENTAL_CONNECTION='Host=<cluster>-rw.awe-d.sutorbank.cloud;Database=app;'\
'Username=app;Password=<passwort>;'\
'SSL Mode=VerifyCA;Root Certificate=/home/coder/sutor-k8s-ca.crt'
dotnet test --filter-trait Exercise=00
```

Das Passwort steht im Secret `<cluster>-app-user`. Übung 0 beschreibt den
Weg dorthin, die Hostnamen für den code-server und die Wahl von
`SSL Mode`. Für Übung 6 kann zusätzlich `RENTAL_CONNECTION_RO` gesetzt
werden; den Host dafür nennt Übung 6. Ohne diese Variable wird der
betroffene Test übersprungen.

### Ablauf

```sh
git checkout -b meine-uebung-01 uebung-01-start
dotnet test --filter-trait Exercise=01
```

Maßgeblich für die Aufgabentexte unter `dotnet/` und die SQL-Serie unter
`sql/` ist der Stand von `main`. Jeder Tag enthält beide samt den Dateien
`loesung.sql` im Stand von `main` beim letzten Aufbau der Tags. Ein zweiter
Worktree hält `main` daneben bereit. Er lässt sich anlegen, sobald `main` nicht mehr im ersten
Arbeitsverzeichnis ausgecheckt ist, also nach dem ersten `git checkout -b`:

```sh
git worktree add ../training-postgres-labs-sql main
```

Ohne Worktree stehen dieselben Dateien auf GitHub im Branch `main` unter
`sql/`.

Die Tests einer Übung sind zu Beginn rot und nennen den fehlenden Baustein.
Bonus-Tests tragen den Trait `Stretch=true` und zählen nicht zur Abnahme:

```sh
dotnet test --filter-trait Exercise=03 --filter-not-trait Stretch=true
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

`AUFGABEN.md` enthält die Übersicht der Übungen. Jede Übung hat unter
`dotnet/` eine Aufgabe mit demselben Aufbau. Sie nennt, worum es geht und
was der Startpunkt enthält, und geht dann Schritt für Schritt durch jede
Datei und Methode. Eine Tabelle nennt zu jedem Test, was er prüft und was
seine Fehlermeldung bedeutet. Am Ende stehen Fallstricke und Bonus.

### Aufbau

```text
Rental/                  Anwendung: Datenbank, Geräte, Buchungen, Import, Modell, Betrieb
Rental.Tests/            Ein Testprojekt, Tests je Übung über den Trait Exercise
dotnet/                  Aufgabenbeschreibungen je Übung
scripts/                 Aufbau der Übungs-Tags
```

Die Anwendung ist eine Klassenbibliothek ohne Web-API; die Tests sind der
einzige Aufrufer. Die Tests bauen das Schema `rental` vor jeder Testklasse
neu auf und lesen den Serverzustand über `pg_stat_activity`,
`pg_prepared_statements` und die Systemkataloge.

### Tags neu aufbauen

Die Tags `uebung-NN-start` und `uebung-NN-loesung` bilden eine Kette von
Commits auf einem Stand von `main`. Jeder Commit der Kette ändert nur
`Rental/` und `Rental.Tests/`. Nach einer Änderung an `main` setzt
`scripts/rebuild-exercise-tags.sh` die Kette auf den aktuellen Stand von
`main` und verschiebt die 13 Tags. Danach übernimmt
`git push --force origin 'refs/tags/uebung-*'` sie auf GitHub; vorhandene
Klone holen sie mit `git fetch --force --tags`.
