# SQL-Übung 0: Soundcheck

## Ziel

Du legst das Schema `tickets` in deiner eigenen Datenbank an und prüfst mit
einer einzigen Abfrage, ob Verbindung, Rechte und Datenbestand für alle
folgenden Übungen stimmen. Danach liest du dasselbe vom Replikat und wirfst
einen ersten Blick in die Daten. Die Domäne ist ein Ticketsystem: Agents in
vier Teams bearbeiten Tickets mit Status, Priorität und JSONB-Metadaten,
dazu Kommentare als Baum.

## Ausgangsstand

Du hast eine eigene Servergruppe im pgAdmin mit einem RW-Server und einem
RO-Server auf deinen Cluster, Datenbank `app`, Rolle `app`. Das Passwort
steht im Secret `<cluster>-app-user` unter dem Schlüssel `password`.
`<cluster>` ist der Name deines Clusters nach dem Muster `<name>-pg`.
`kubectl` meldet sich über Pinniped per SSO an, der Kontext heißt
`awe-d-pinniped`:

```bash
kubectl --context awe-d-pinniped -n training-postgres \
  get secret <cluster>-app-user -o jsonpath='{.data.password}' | base64 -d
```

Ohne `kubectl` findest du das Secret in Headlamp unter `Configuration`,
`Secrets`, `<cluster>-app-user`.

## Aufgaben

1. Öffne das Query Tool auf dem RW-Server. Stelle `Auto commit` an und
   `Auto rollback on error` aus.

   Für alle Übungen gilt: Ein Codeblock ist eine Ausführung. Kopiere den
   Block ins Query Tool, markiere ihn vollständig und führe ihn mit F5 aus.
   Erst danach folgt der nächste Block. Enthält ein Block mehrere
   Anweisungen, schickt pgAdmin sie zusammen. Ohne eigenes `BEGIN` laufen
   sie als eine Transaktion, und `Data Output` zeigt nur das Ergebnis der
   letzten. Solche Blöcke sind so gewollt, der Text sagt es jeweils dazu.

2. Öffne `sql/00-einrichtung/setup.sql` aus dem Labs-Repository, kopiere den
   Inhalt ins Query Tool und führe ihn vollständig aus. Am Ende steht die
   Kontrollabfrage mit `50 | 800000 | 2000678`. Nebenbei hält `setup.sql`
   Beginn, Ende und WAL-Position des Laufs in `tickets.setup_run` fest.

3. Öffne `sql/00-einrichtung/soundcheck.sql`, kopiere den Inhalt ins Query
   Tool des RW-Servers und führe ihn als eine Ausführung aus. Jede Zeile ist
   eine Prüfung mit `status` `OK` oder `PRÜFEN` und dem gelesenen Wert in
   `detail`. Die Abfrage liest nur und lässt sich beliebig oft wiederholen.

   Die letzten drei Zeilen zeigen deinen Cluster in Zahlen: wie lange
   `setup.sql` lief, wie viel WAL der Aufbau geschrieben hat und wie groß
   die Datenbank jetzt ist. Die Kursleitung zeigt dieselben Zahlen für alle
   Cluster auf dem Beamer.

4. Öffne ein Query Tool auf dem RO-Server, Datenbank `app`, und führe aus:

   ```sql
   SELECT pg_is_in_recovery() AS is_replica,
          pg_last_wal_receive_lsn() = pg_last_wal_replay_lsn()
              AS replay_complete,
          date_trunc('second', now() - pg_last_xact_replay_timestamp())
              AS last_replay_age,
          (SELECT date_trunc('second', finished_at) FROM tickets.setup_run)
              AS setup_finished_at;
   ```

   `replay_complete` ist `t`, wenn das Replikat alles empfangene WAL
   eingespielt hat. `last_replay_age` nennt, wie lange die zuletzt
   eingespielte Transaktion zurückliegt. `setup_finished_at` ist derselbe
   Zeitpunkt wie auf dem RW-Server: Der Datenbestand kam über die
   Replikation an.

5. Wirf im Query Tool des RW-Servers einen ersten Blick in die Daten. Die
   Abfrage verteilt die Tickets auf die Teams ihrer Agents:

   ```sql
   SELECT coalesce(a.team, '(kein Agent)') AS team,
          count(DISTINCT a.id) AS agents,
          count(*) AS tickets,
          count(*) FILTER (WHERE t.status <> 'closed') AS not_closed,
          count(*) FILTER (WHERE t.status = 'open') AS open
   FROM tickets.ticket AS t
   LEFT JOIN tickets.agent AS a ON a.id = t.agent_id
   GROUP BY a.team
   ORDER BY a.team NULLS LAST;
   ```

6. Suche die Tabellen im Object Explorer unter `app > Schemas > tickets`.
   Nach dem Lauf zeigt der Objektbaum das Schema erst nach `Refresh` im
   Kontextmenü.

## Ergebnis prüfen

`soundcheck.sql` meldet in jeder Zeile `OK`. Referenzlauf auf dem
Kurscluster `trainer-pg` am 25.09.2026:

```text
     check_name      | status |                   detail
---------------------+--------+--------------------------------------------
 Rolle und Datenbank | OK     | app in app, Eigentümer app, kein Superuser
 PostgreSQL 18.6     | OK     | 18.6 (Debian 18.6-1.pgdg13+2)
 Primärinstanz       | OK     | pg_is_in_recovery() = false
 TLS                 | OK     | TLSv1.3 TLS_AES_256_GCM_SHA384
 Auto commit         | OK     | keine offene Transaktion
 Testdaten           | OK     | 50 | 800000 | 2000678
 Anlegen in tickets  | OK     | CREATE auf Schema tickets
 postgres_fdw        | OK     | Version 1.2, USAGE vorhanden
 Laufzeit setup.sql  | OK     | 141 s
 WAL von setup.sql   | OK     | 494 MB
 Größe der Datenbank | OK     | 849 MB
(11 rows)
```

Laufzeit und Datenbankgröße unterscheiden sich von Cluster zu Cluster. Auf
`trainer-pg` liegen neben `tickets` weitere Schemas der Trainer-Demos. Das
Schema `tickets` allein belegt rund 420 MB.

Der Check auf dem RO-Server:

```text
 is_replica | replay_complete | last_replay_age |   setup_finished_at
------------+-----------------+-----------------+------------------------
 t          | t               | 00:02:31        | 2026-09-25 19:18:38+00
(1 row)
```

Der erste Blick in die Daten:

```text
     team     | agents | tickets | not_closed | open
--------------+--------+---------+------------+------
 Billing      |     19 |  273940 |      13549 | 4530
 Onboarding   |      8 |  114676 |       5677 | 1861
 Retention    |      7 |  100992 |       5025 | 1674
 Technical    |     16 |  230551 |      11395 | 3784
 (kein Agent) |      0 |   79841 |       4038 | 1295
(5 rows)
```

Die Werte hängen nur von `setup.sql` ab und sind in jeder Datenbank gleich.
Rund 95 Prozent der Tickets sind geschlossen, jedes zehnte hat keinen
Agent. Die 3784 offenen Tickets des Teams `Technical` kommen in Übung 4
wieder vor.

## Hinweise

Was bei `PRÜFEN` zu tun ist:

- `Rolle und Datenbank`: Das Query Tool gehört zu einem anderen Server oder
  einer anderen Datenbank. Öffne es auf dem RW-Server und der Datenbank
  `app`. Steht dort `Superuser` oder ein anderer Eigentümer, sag der
  Kursleitung Bescheid.
- `PostgreSQL 18.6`: Der Cluster hat eine andere Version. Sag der
  Kursleitung Bescheid.
- `Primärinstanz`: Das Query Tool läuft auf dem RO-Server. Nimm das Query
  Tool des RW-Servers.
- `TLS`: `unverschlüsselt` oder eine ältere TLS-Version. Die
  Servereinstellung im pgAdmin stimmt nicht; sag der Kursleitung Bescheid.
- `Auto commit`: `Auto commit` ist aus, oder eine Transaktion aus einer
  früheren Ausführung ist noch offen. Führe `ROLLBACK;` aus, stelle
  `Auto commit` an und wiederhole den Soundcheck.
- `Testdaten`: `Schema tickets fehlt`, `kein ANALYZE` oder andere Zahlen.
  Führe `setup.sql` vollständig aus.
- `Anlegen in tickets`: Bei `Schema tickets fehlt` führe `setup.sql` aus.
  Bei `kein CREATE auf Schema tickets` sag der Kursleitung Bescheid.
- `postgres_fdw`: `Extension fehlt` oder `kein USAGE`. Sag der Kursleitung
  Bescheid; die Extension braucht erst Übung 15.
- `Laufzeit setup.sql` und `WAL von setup.sql`: Fehlt die Angabe in
  `tickets.setup_run`, stammt das Schema aus einer älteren Fassung von
  `setup.sql`. Führe `setup.sql` erneut aus.

`soundcheck.sql` läuft auch vor `setup.sql`. Dann melden die Zeilen zum
Schema `tickets` `PRÜFEN`, die übrigen zeigen schon, ob die Verbindung
stimmt. `Testdaten` liest die Zeilenzahlen aus der Statistik, die
`setup.sql` am Ende mit `ANALYZE` erhebt. Nach späteren Übungen, die
Zeilen ändern, können sie abweichen; ein erneuter Lauf von `setup.sql`
stellt den Ausgangsstand wieder her.

Auf dem Kurscluster `trainer-pg` dauerten Läufe von `setup.sql` am
25.09.2026 zwischen 141 und 275 Sekunden, gegen eine lokale Testinstanz
35 bis 45 Sekunden. Das Schema belegt danach rund 420 MB. Ein erneuter Lauf
von `setup.sql` löscht das Schema `tickets` samt aller Übungsobjekte darin
und baut es neu auf. Mengen und Werte bleiben dabei gleich.

Bleibt der Lauf hängen und bricht nach fünf Sekunden mit
`ERROR: canceling statement due to lock timeout` ab, hat ein anderer
Query-Tool-Tab noch eine offene Transaktion auf einer Tabelle im Schema
`tickets`. Schließe diesen Tab oder führe dort `ROLLBACK;` aus. Führe
danach auch in deinem eigenen Query Tool `ROLLBACK;` aus, denn `setup.sql`
hat dort selbst eine Transaktion geöffnet, die jetzt abgebrochen ist. Ohne
diesen Schritt endet der nächste Lauf sofort mit `25P02`
(`current transaction is aborted`). Starte `setup.sql` dann erneut.

Bricht der Lauf mit `No space left on device` ab, ist das Volume deines
Clusters voll. Sag der Kursleitung Bescheid.

`setup.sql` ist eine Datei und kein Codeblock. Sie steuert ihre
Transaktionen mit `BEGIN` und `COMMIT` selbst. pgAdmin zeigt nur das
letzte Ergebnis einer Ausführung an, die Kontrollabfrage steht deshalb am
Ende der Datei.

Auf dem RW-Server zeigt der Check aus Aufgabe 4 `is_replica` `f`. Die
Spalten zur Replikation stammen dann aus einer früheren Recovery der
Instanz oder bleiben leer. Ohne Schreiblast wächst `last_replay_age`, obwohl
das Replikat nichts nachzuholen hat; maßgeblich ist `replay_complete`.

Die .NET-Serie hat ihren eigenen Soundcheck: Übung 0 unter
`dotnet/00-einrichtung` im code-server prüft Verbindung, Rolle, Version
und Primärinstanz mit `dotnet test --filter-trait Exercise=00`.
