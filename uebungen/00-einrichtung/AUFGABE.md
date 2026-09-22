# Übung 0: Einrichtung, 15 Minuten

## Ziel

Die Tests erreichen die eigene Datenbank auf dem Cluster. Es gibt keinen
Code zu schreiben; alle Tests dieser Übung sind grün, sobald die Umgebung
stimmt.

## Ausgangslage

Das Repository ist geklont, `VERLEIH_CONNECTION` ist noch leer. Jede Person
hat eine eigene Datenbank und ist deren Eigentümerin.

## Aufgabe

Die Verbindungszeichenfolge aus dem Secret der eigenen Datenbank ableiten und
als Umgebungsvariable setzen. Bei CloudNativePG heißt das Secret
`<cluster>-app` oder wie vom Trainer genannt; es enthält `host`, `port`,
`dbname`, `user` und `password`.

```sh
kubectl get secret <secret> -o go-template='{{range $k,$v := .data}}{{$k}}={{$v | base64decode}}{{"\n"}}{{end}}'
export VERLEIH_CONNECTION='Host=<host>;Port=<port>;Database=<dbname>;Username=<user>;Password=<password>'
dotnet test --filter-trait Uebung=00
```

## Anforderungen

| Test                                     | Prüft                                                   |
| ---------------------------------------- | ------------------------------------------------------- |
| `Verbindung_erreicht_eigene_Datenbank`   | `current_database()` entspricht der Datenbank im Secret |
| `Rolle_ist_Eigentuemerin_ohne_Superuser` | Eigentümerin laut `pg_database`, `rolsuper = false`     |
| `Serverversion_ist_17_oder_18`           | `server_version_num`                                    |
| `Zugriff_geht_auf_den_rw_Dienst`         | `pg_is_in_recovery() = false`                           |

## Fertig, wenn

`dotnet test --filter-trait Uebung=00` vier grüne Tests meldet.
