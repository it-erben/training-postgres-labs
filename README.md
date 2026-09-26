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
Kunden und Buchungen. `main` enthält den Startstand aller Übungen: Tests und
Stubs liegen von Beginn an in `Rental/` und `Rental.Tests/`, die Tests einer
Übung sind zu Beginn rot. Jede Übung hat eine Aufgabenbeschreibung unter
`dotnet/` und eine Musterlösung unter `answers/dotnet/`. Wer die Tests einer
Übung grün hat, ist mit ihr fertig.

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

Alle Übungen laufen im selben Arbeitsverzeichnis auf `main`. Der Trait
`Exercise` wählt die Tests einer Übung aus:

```sh
dotnet test --filter-trait Exercise=01
```

Die Tests einer Übung sind zu Beginn rot und nennen den fehlenden Baustein.
Bonus-Tests tragen den Trait `Stretch=true` und zählen nicht zur Abnahme:

```sh
dotnet test --filter-trait Exercise=03 --filter-not-trait Stretch=true
```

Jede Übung baut auf den Lösungen der vorigen auf. Die Tests von Übung 3
brauchen etwa die DataSource aus Übung 1 und den `BookingStore` aus Übung 2.

### Musterlösungen

`answers/dotnet/NN-thema/` enthält die Dateien, die Übung NN löst, unter
ihrem Pfad im Repository. Die Datei ersetzt die eigene Fassung. Eine Datei,
die zwei Übungen ändern, liegt in der späteren Übung mit beiden Lösungen:
`answers/dotnet/02-typen/Rental/Database/RentalDataSource.cs` enthält die
Lösung aus Übung 1 und die Erweiterung aus Übung 2.

Wer eine Übung nicht abschließt, übernimmt ihre Musterlösung und arbeitet
mit der nächsten Übung weiter:

```sh
cp -R answers/dotnet/01-verbindungen/. .
```

Mehrere Übungen werden in ihrer Reihenfolge übernommen. Die Musterlösungen
der SQL-Serie liegen unter `answers/sql/`.

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
answers/dotnet/          Musterlösungen je Übung, Pfade wie im Repository
answers/sql/             Musterlösungen der SQL-Serie
```

Die Anwendung ist eine Klassenbibliothek ohne Web-API; die Tests sind der
einzige Aufrufer. Die Tests bauen das Schema `rental` vor jeder Testklasse
neu auf und lesen den Serverzustand über `pg_stat_activity`,
`pg_prepared_statements` und die Systemkataloge.
