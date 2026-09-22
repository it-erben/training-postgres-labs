# Übung 0: Einrichtung, 15 Minuten

## Worum es geht

Alle folgenden Übungen laufen gegen eine PostgreSQL-Datenbank, die nur dir
gehört. Die Tests bauen darin vor jeder Testklasse das Schema `rental`
neu auf, legen Tabellen an, verwerfen sie wieder und lesen dabei den
Zustand des Servers über `pg_stat_activity` und die Systemkataloge. Dafür
brauchen sie eine Verbindung, die vier Bedingungen erfüllt: eigene
Datenbank, Eigentümerrolle ohne Superuser, PostgreSQL 17 oder 18, und der
Zugriff geht auf den Primärserver, nicht auf ein Replikat.

In dieser Übung schreibst du keinen Code. Du setzt eine Umgebungsvariable
und prüfst mit vier Tests, dass die Umgebung stimmt. Alles Weitere baut
darauf auf; wenn hier etwas rot ist, wird jede spätere Übung daran scheitern.

## Was du vorfindest

```text
Rental/                              Anwendung, in dieser Übung noch leer
Rental.Tests/
  Infrastructure/TestEnvironment.cs  liest RENTAL_CONNECTION und baut eine Test-DataSource
  Exercise00Setup.cs                 die vier Tests dieser Übung
```

`TestEnvironment.ConnectionString` wirft eine Ausnahme mit Hinweis, wenn die
Variable fehlt. `TestEnvironment.CreateAdminSource()` liefert eine
`NpgsqlDataSource` mit `Application Name = rental_test`; so unterscheiden
spätere Tests ihre eigenen Verbindungen von denen deiner Anwendung.

## Schritt für Schritt

### 1. Zugangsdaten aus dem Secret lesen

Bei CloudNativePG liegen die Zugangsdaten deiner Datenbank in einem
Kubernetes-Secret. Den Namen nennt der Trainer; üblich ist
`<cluster>-app` oder ein Name je Teilnehmer. Das Secret enthält die
Schlüssel `host`, `port`, `dbname`, `user` (oder `username`) und
`password`.

```sh
kubectl get secret <secret-name> -o go-template='{{range $k,$v := .data}}{{$k}}={{$v | base64decode}}{{"\n"}}{{end}}'
```

Der Host ist der Dienst `<cluster>-rw`. Er zeigt immer auf den
Primärserver; `-ro` und `-r` zeigen auf Replikate und sind für Übung 6
vorgesehen.

### 2. Verbindungszeichenfolge setzen

Npgsql verwendet Schlüssel-Wert-Paare, getrennt durch Semikolon:

```sh
export RENTAL_CONNECTION='Host=<host>;Port=<port>;Database=<dbname>;Username=<user>;Password=<password>'
```

Die Variable gilt nur in der Shell, in der sie gesetzt wurde. Wer die
Tests aus VS Code startet, setzt sie in dem Terminal, aus dem `code` oder
`dotnet` gestartet wird, oder in der Test-Konfiguration des Editors.
Zugangsdaten gehören nie in eine Datei im Repository.

### 3. Tests ausführen

```sh
dotnet test --filter-trait Exercise=00
```

Der erste Lauf stellt NuGet-Pakete wieder her und dauert eine Weile.
Danach erscheinen vier Tests. Der Filter `--filter-trait` wählt Tests
nach ihrem Trait aus; jede Übung hat ihren eigenen Wert.

## Die Tests im Detail

| Test                              | Was er tut                                                                      | Wenn er rot ist                                                                            |
| --------------------------------- | ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `Connection_reaches_own_database` | Vergleicht `current_database()` mit `Database=` aus der Verbindungszeichenfolge | Verbindung schlägt fehl oder zeigt auf eine andere Datenbank; Host, Port, Datenbank prüfen |
| `Role_is_owner_without_superuser` | Liest `pg_database.datdba` und `pg_roles.rolsuper`                              | Die Rolle gehört nicht zur Datenbank oder ist Superuser; beim Trainer melden               |
| `Server_version_is_17_or_18`      | Liest `server_version_num`                                                      | Der Cluster hat eine andere Version; Übungen sind für 17 und 18 geprüft                    |
| `Access_goes_to_rw_service`       | Prüft `pg_is_in_recovery() = false`                                             | Die Verbindung landet auf einem Replikat; Host auf `-rw` ändern                            |

Eine Meldung wie `RENTAL_CONNECTION ist nicht gesetzt` bedeutet, dass die
Shell die Variable nicht kennt. `28P01` ist ein falsches Passwort, `3D000`
eine unbekannte Datenbank, `08001` oder ein Timeout ein falscher Host.

## Fertig, wenn

`dotnet test --filter-trait Exercise=00` vier grüne Tests meldet. Danach
beginnt Übung 1 mit `git checkout -b meine-uebung-01 uebung-01-start`.
