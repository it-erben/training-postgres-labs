# Übung 6: Betrieb, 30 Minuten

## Ziel

Die Rückgabe eines Geräts benachrichtigt Empfänger im selben COMMIT. Ein
Empfänger hält eine dedizierte Verbindung. Die Anwendung sieht ihre eigenen
Serververbindungen. Lesende Zugriffe bevorzugen ein Replikat.

## Ausgangslage

`Verleih/Betrieb/Rueckgabe.cs` enthält Stubs für `RueckgabeDienst`,
`RueckgabeMelder`, `Diagnose` und `Lesequelle`. Die Migration
`006_rueckgabe.sql` ergänzt `buchung.zurueckgegeben_am`.

## Aufgabe

1. `RueckgabeDienst.ZurueckgebenAsync`: `UPDATE` und `pg_notify` in einer
   Transaktion; der Hook `VorCommit` läuft nach `NOTIFY` und vor `COMMIT`.
2. `RueckgabeMelder.LaufeAsync`: eigene Verbindung ohne Pool mit
   `Application Name = verleih_melder` und `Keepalive`, `LISTEN`, dann
   `WaitAsync` in einer Schleife bis zum Abbruch. Die Verbindungszeichenfolge
   ist über `Verbindungszeichenfolge` lesbar.
3. `Diagnose.EigeneVerbindungenAsync`: Verbindungen der Anwendung aus
   `pg_stat_activity`, nur die eigenen Application Names.
4. `Lesequelle.Erzeuge`: DataSource mit `Target Session Attributes =
   prefer-standby` für eine Verbindungszeichenfolge mit mehreren Hosts.

## Anforderungen

| Test                                                            | Prüft                                                        |
| --------------------------------------------------------------- | ------------------------------------------------------------ |
| `Rueckgabe_sendet_Benachrichtigung_nach_Commit`                 | Payload ist die Buchungsnummer                               |
| `Benachrichtigung_kommt_nicht_vor_dem_Commit`                   | `WaitAsync(500)` im Hook ist `false`                         |
| `Zurueckgerollte_Rueckgabe_sendet_nichts`                       | Ausnahme im Hook: kein Empfang, keine Änderung, keine offene Transaktion |
| `Empfaenger_verwendet_eine_dedizierte_Verbindung_mit_Keepalive` | `Keepalive > 0`, genau eine Verbindung `verleih_melder`      |
| `Diagnose_listet_eigene_Verbindungen_mit_Zustand`               | Melder `idle`, Anwendung `active`, keine Testverbindungen    |
| `Leseabfragen_bevorzugen_das_Replikat`                          | `pg_is_in_recovery()` über `VERLEIH_CONNECTION_RO`; ohne Variable übersprungen |

## Hinweise

- [Npgsql: Waiting for Notifications](https://www.npgsql.org/doc/wait.html)
- [Npgsql: Failover and Load Balancing](https://www.npgsql.org/doc/failover-and-load-balancing.html)
- [PostgreSQL: NOTIFY](https://www.postgresql.org/docs/18/sql-notify.html)
- Benachrichtigungen folgen der Transaktion des Senders.
- `Pooling = false` für den Melder: Die Verbindung darf nicht zwischen
  Aufgaben wandern.

## Bonus

`Melder_verbindet_nach_Verbindungsabbruch_neu`: Der Test beendet die
Melderverbindung mit `pg_terminate_backend`; der Melder baut sie neu auf und
empfängt weiterhin.

## Fertig, wenn

`dotnet test --filter-trait Uebung=06 --filter-not-trait Stretch=ja` fünf
grüne Tests meldet und einer übersprungen ist, falls kein Replikat
konfiguriert wurde.
