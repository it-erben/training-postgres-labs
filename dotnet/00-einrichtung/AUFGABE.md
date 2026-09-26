# Übung 0: Einrichtung

## Worum es geht

Alle folgenden Übungen laufen gegen eine PostgreSQL-Datenbank, die nur dir
gehört. Die Tests bauen darin vor jeder Testklasse das Schema `rental`
neu auf, legen Tabellen an, verwerfen sie wieder und lesen dabei den
Zustand des Servers über `pg_stat_activity` und die Systemkataloge. Dafür
brauchen sie eine Verbindung, die vier Bedingungen erfüllt: eigene
Datenbank, Eigentümerrolle ohne Superuser, PostgreSQL 17 oder 18 und
Zugriff auf den Primärserver.

In dieser Übung schreibst du keinen Code. Du setzt eine Umgebungsvariable
und prüfst mit vier Tests, dass die Umgebung stimmt. Alles Weitere baut
darauf auf. Ist hier ein Test rot, scheitert jede spätere Übung daran.

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

Deine Datenbank heißt `app`, deine Rolle ebenfalls `app`. `<cluster>`
steht für den Namen deines CloudNativePG-Clusters nach dem Muster
`<name>-pg`. Das Passwort liegt im Kubernetes-Secret `<cluster>-app-user`
im Namespace `training-postgres`, Schlüssel `password`. Host, Port und
Datenbank stehen nicht im Secret.

`kubectl` meldet sich über Pinniped an. Pinniped-CLI und Kubeconfig
stellt die Kursleitung bereit, der Kontext heißt `awe-d-pinniped`. Die
Anmeldung läuft per SSO.

```sh
kubectl --context awe-d-pinniped -n training-postgres \
  get secret <cluster>-app-user -o jsonpath='{.data.password}' | base64 -d
```

Ohne `kubectl` zeigt Headlamp dasselbe Secret unter `Configuration`,
`Secrets`, `<cluster>-app-user`.

### 2. Host und TLS wählen

Dein Cluster hat zwei Instanzen, eine Primärinstanz und ein Replikat. Im
Kubernetes-Cluster heißen die Dienste `<cluster>-rw` (Primärinstanz),
`<cluster>-ro` (nur Replikate) und `<cluster>-r` (alle Instanzen
einschließlich der Primärinstanz). Der code-server läuft in einem anderen
Kubernetes-Cluster und löst diese Namen nicht auf. Er erreicht die
Datenbank über LoadBalancer-Namen:

| Zweck                       | Host                                 |
| --------------------------- | ------------------------------------ |
| Alle Übungen, Primärinstanz | `<cluster>-rw.awe-d.sutorbank.cloud` |
| Übung 6, Lesen vom Replikat | `<cluster>-ro.awe-d.sutorbank.cloud` |

Beide LoadBalancer nehmen nur Verbindungen aus dem Netz `10.111.10.0/24`
an.

Das Serverzertifikat nennt nur die internen Dienstnamen, den
LoadBalancer-Namen nicht. Daraus ergibt sich die Einstellung für
`SSL Mode`:

- `VerifyCA` prüft die Zertifikatskette gegen die CA-Datei. Das ist die
  Einstellung für die Kursumgebung.
- `Require` verschlüsselt nur und prüft den Server nicht. Nur verwenden,
  wenn die CA-Datei fehlt.
- `VerifyFull` prüft Kette und Hostnamen. Das ist die Einstellung für
  Produktion; hier scheitert sie am LoadBalancer-Namen.

Die CA-Datei liegt unter `/home/coder/sutor-k8s-ca.crt`. Die
Kursleitung legt sie dort ab. Dasselbe CA-Zertifikat steht auch in der
ConfigMap `internal-truststore` im Namespace `training-postgres`. Die
Datei gehört nicht ins Repository.

```sh
ls -l /home/coder/sutor-k8s-ca.crt
```

Fehlt die Datei, sag der Kursleitung Bescheid und arbeite bis dahin mit
`SSL Mode=Require` ohne `Root Certificate`. Die Verbindung bleibt dann
verschlüsselt, prüft aber nicht, mit welchem Server sie spricht.

In Produktion gilt `VerifyFull`. Dafür muss der Hostname, über den die
Anwendung verbindet, im Zertifikat stehen.

### 3. Verbindungszeichenfolge setzen

Npgsql verwendet Schlüssel-Wert-Paare, getrennt durch Semikolon. Die
Shell setzt die drei Teile in Hochkommas zu einer Zeichenfolge zusammen:

```sh
export RENTAL_CONNECTION='Host=<cluster>-rw.awe-d.sutorbank.cloud;Database=app;'\
'Username=app;Password=<password>;'\
'SSL Mode=VerifyCA;Root Certificate=/home/coder/sutor-k8s-ca.crt'
```

Ohne `Port` verwendet Npgsql 5432. Nennt die Kursleitung einen anderen
Port, kommt `Port=<port>` dazu.

Die Variable gilt nur in der Shell, in der sie gesetzt wurde. Wer die
Tests aus VS Code startet, setzt sie in dem Terminal, aus dem `code` oder
`dotnet` gestartet wird, oder in der Test-Konfiguration des Editors.
Zugangsdaten gehören nie in eine Datei im Repository.

### 4. Tests ausführen

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
Shell die Variable nicht kennt. Weitere Fehlerbilder:

- `28P01`: falsches Passwort. Erneut aus `<cluster>-app-user` lesen.
- `3D000`: unbekannte Datenbank. `Database=app` prüfen.
- `08001` oder ein Timeout: falscher Host, etwa der interne Dienstname
  `<cluster>-rw` im code-server.
- Abbruch bei der Zertifikatsprüfung: `SSL Mode=VerifyFull` mit dem
  LoadBalancer-Namen. `VerifyCA` verwenden.
- Fehler zur Datei aus `Root Certificate`: Die CA-Datei fehlt oder der
  Pfad stimmt nicht. Mit `ls -l` prüfen, sonst `Require`.

## Fertig, wenn

`dotnet test --filter-trait Exercise=00` vier grüne Tests meldet. Übung 1
läuft im selben Arbeitsverzeichnis weiter.
