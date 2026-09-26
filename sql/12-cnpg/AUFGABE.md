# SQL-Übung 12: CloudNativePG aus Anwendersicht

## Ziel

Das Ticketsystem liegt in deinem eigenen CloudNativePG-Cluster mit einer
Primärinstanz und einem Replikat. Beide unterscheidest du mit SQL. Dann
prüfst du, ob deine Verbindung verschlüsselt ist, und beobachtest, wann eine
Zeile vom RW-Server auf dem RO-Server ankommt. Zum Schluss liest du Status
und Einstellungen deines Clusters und begründest für drei Anwendungsfälle,
welchen Dienst die Anwendung verwenden soll.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus. Jeder Codeblock
ist eine Ausführung, wie in Übung 0 beschrieben.

In deiner Servergruppe im pgAdmin gibt es zwei Server auf denselben
Cluster:

| Server | Host                                               | Ziel          |
| ------ | -------------------------------------------------- | ------------- |
| RW     | `<cluster>-rw.training-postgres.svc.cluster.local` | Primärinstanz |
| RO     | `<cluster>-ro.training-postgres.svc.cluster.local` | nur Replikate |

Öffne für beide Server ein eigenes Query Tool auf der Datenbank `app`.
Entferne zu Beginn auf dem RW-Server die Übungstabelle:

```sql
DROP TABLE IF EXISTS tickets.read_test;
```

Für Aufgabe 4 brauchst du entweder `kubectl` mit Leserechten im Namespace
`training-postgres` oder Zugang zu Headlamp. In der Kursumgebung stehen
beide Wege offen; `kubectl` läuft mit dem Kontext `awe-d-pinniped` wie in
[Übung 0](../00-einrichtung/AUFGABE.md).

## Aufgaben

1. Führe auf dem RW-Server und auf dem RO-Server dieselbe Abfrage aus und
   vergleiche die Ergebnisse:

   ```sql
   SELECT pg_is_in_recovery() AS is_replica,
          inet_server_addr() AS server_addr,
          current_setting('transaction_read_only') AS read_only;
   ```

   `pg_is_in_recovery()` ist `t` auf einem Replikat, das laufend WAL der
   Primärinstanz einspielt. `inet_server_addr()` ist die Adresse des Pods,
   an dem die Verbindung endet. Beide Server zeigen verschiedene Adressen,
   obwohl sie zum selben Cluster gehören.

   Referenzlauf auf dem Kurscluster `trainer-pg` am 25.09.2026, zuerst RW,
   dann RO. Die Verbindung lief dort über `kubectl port-forward`, deshalb
   zeigt `server_addr` auf beiden Servern die Loopback-Adresse im Pod:

   ```text
    is_replica | server_addr | read_only 
   ------------+-------------+-----------
    f          | 127.0.0.1   | off
   (1 row)

    is_replica | server_addr | read_only 
   ------------+-------------+-----------
    t          | 127.0.0.1   | on
   (1 row)
   ```

   Im pgAdmin steht in `server_addr` die Adresse des Pods. Für `trainer-pg`
   nannte `kubectl get pods -o wide` am selben Tag `100.64.3.140` für die
   Primärinstanz `trainer-pg-1` und `100.64.1.16` für das Replikat
   `trainer-pg-2`.

2. Lies aus `pg_stat_ssl` ab, ob deine eigene Verbindung verschlüsselt ist,
   mit welcher TLS-Version und mit welchem Verfahren. `pg_stat_ssl` hat
   eine Zeile je Serverprozess; `pg_backend_pid()` liefert die Prozess-ID
   deiner Sitzung.

3. Lege auf dem RW-Server die Tabelle `tickets.read_test` an und schreibe
   eine Zeile. Beide Anweisungen laufen als eine Ausführung:

   ```sql
   CREATE TABLE tickets.read_test (
       id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
       note text NOT NULL,
       written_at timestamptz NOT NULL DEFAULT now()
   );
   INSERT INTO tickets.read_test (note) VALUES ('geschrieben auf RW');
   ```

   Wechsle sofort in das Query Tool des RO-Servers und lies die Tabelle.
   Notiere dort `pg_last_xact_replay_timestamp()` und vergleiche den Wert
   mit `written_at`. Versuche danach, auf dem RO-Server eine zweite
   Zeile einzufügen.

4. Lies den Status deines Clusters. Wähle den Weg, den deine Rechte
   erlauben:

   - `kubectl -n training-postgres get cluster <cluster>` und
     `kubectl -n training-postgres get pods -l cnpg.io/cluster=<cluster> -L cnpg.io/instanceRole`
   - Headlamp: `Custom Resources`, Gruppe `postgresql.cnpg.io`, `Cluster`,
     dann dein Cluster

   Notiere die Phase, den Namen der Primärinstanz und die Zahl der
   Instanzen. Welcher Pod gehört zur Adresse, die Aufgabe 1 auf dem
   RW-Server gezeigt hat? Mit `-o wide` zeigt `kubectl` die Adresse jedes
   Pods; in Headlamp steht sie unter `Workloads`, `Pods` in der Spalte `IP`.

   Referenzlauf für `trainer-pg` am 25.09.2026, die Pod-Liste mit
   `-o wide` und ohne die Spalten zum Knoten:

   ```text
   NAME         AGE   INSTANCES   READY   STATUS                     PRIMARY
   trainer-pg   24h   2           2       Cluster in healthy state   trainer-pg-1

   NAME           READY   STATUS    RESTARTS      AGE    IP             INSTANCEROLE
   trainer-pg-1   2/2     Running   0             108m   100.64.3.140   primary
   trainer-pg-2   2/2     Running   1 (99m ago)   24h    100.64.1.16    replica
   ```

   `READY 2/2` zählt zwei Container je Pod: PostgreSQL und das Plugin, das
   WAL und Backups in den Objektspeicher schreibt. Bei den Clustern der
   Teilnehmenden war am selben Tag `<cluster>-2` die Primärinstanz.

   Lies zum Schluss die Einstellungen deiner Primärinstanz auf dem
   RW-Server:

   ```sql
   SELECT name, setting, unit, source
   FROM pg_settings
   WHERE name IN ('server_version', 'max_connections', 'shared_buffers',
                  'work_mem', 'maintenance_work_mem', 'max_wal_size',
                  'wal_level', 'synchronous_commit',
                  'synchronous_standby_names', 'archive_mode', 'ssl',
                  'io_method', 'default_transaction_isolation', 'TimeZone',
                  'idle_in_transaction_session_timeout', 'statement_timeout')
   ORDER BY name;
   ```

   Vergleiche das Ergebnis mit dem Stand der Kursumgebung. `source` zeigt,
   woher ein Wert kommt: `configuration file` stammt aus der
   Cluster-Ressource, `default` ist der Standard von PostgreSQL.

   Stand der Kursumgebung am 25.09.2026, gelesen auf `trainer-pg` und
   stichprobenartig auf einem Cluster der Teilnehmenden. Die ersten vier
   Zeilen zeigt `kubectl`: `get cluster`, die Annotation
   `cnpg.io/operatorVersion` in `describe pod <cluster>-1`, `get pvc` und
   `get poolers`. Die übrigen liefert die Abfrage oben:

   | Eigenschaft                           | Wert                           |
   | ------------------------------------- | ------------------------------ |
   | Instanzen                             | 2: Primärinstanz und Replikat  |
   | CloudNativePG-Operator                | 1.30.0                         |
   | Speicher                              | 8Gi je Instanz, samt WAL       |
   | Pooler                                | keiner                         |
   | `server_version`                      | 18.6                           |
   | `max_connections`                     | 100, davon 3 für Superuser     |
   | `shared_buffers`                      | 4096 × 8kB = 32 MB             |
   | `work_mem`                            | 4096 kB                        |
   | `maintenance_work_mem`                | 65536 kB                       |
   | `max_wal_size`                        | 1024 MB                        |
   | `wal_level`                           | `logical`                      |
   | `synchronous_commit`                  | `on`                           |
   | `synchronous_standby_names`           | leer, also asynchron           |
   | `archive_mode`                        | `on`, Archiv im Objektspeicher |
   | `ssl`                                 | `on`, nur TLS 1.3              |
   | `io_method`                           | `worker`                       |
   | `default_transaction_isolation`       | `read committed`               |
   | `TimeZone`                            | `Etc/UTC`                      |
   | `idle_in_transaction_session_timeout` | 0, keine Grenze                |
   | `statement_timeout`                   | 0, keine Grenze                |

5. Wähle für drei Anwendungsfälle zwischen `<cluster>-rw`, `<cluster>-ro`
   und `<cluster>-r` und begründe die Wahl in einem Satz:

   - Eine Agentin übernimmt ein Ticket.
   - Nach dem Speichern zeigt die Anwendung den eigenen neuen Kommentar an.
   - Ein Monatsbericht zählt die geschlossenen Tickets des Vormonats.

## Ergebnis prüfen

Auf dem RO-Server ausführen:

```sql
SELECT pg_is_in_recovery() AS is_replica,
       (SELECT count(*) FROM tickets.read_test) AS row_count;
```

Referenzlauf auf `trainer-pg`:

```text
 is_replica | row_count 
------------+-----------
 t          |         1
(1 row)
```

Steht in `is_replica` ein `f`, lief die Abfrage auf dem RW-Server. Meldet der
RO-Server `42P01` (`relation "tickets.read_test" does not exist`), fehlt
Aufgabe 3 auf dem RW-Server.

Aufgabe 2 zeigte im selben Referenzlauf auf beiden Servern:

```text
 ssl | version |         cipher         | bits 
-----+---------+------------------------+------
 t   | TLSv1.3 | TLS_AES_256_GCM_SHA384 |  256
(1 row)
```

## Hinweise

Der Schreibversuch auf dem RO-Server endet mit `25006`
`cannot execute INSERT in a read-only transaction`. Auf einem Replikat
ist jede Transaktion schreibgeschützt; `transaction_read_only` steht dort
auf `on`.

`pg_last_xact_replay_timestamp()` ist der Commit-Zeitpunkt der zuletzt
eingespielten Transaktion, gemessen auf der Primärinstanz. Auf `trainer-pg`
lag er 3 bis 7 Millisekunden nach `written_at`. `written_at` ist `now()`,
der Beginn der Transaktion; der Abstand enthält deshalb auch die Dauer von
`CREATE TABLE` und `INSERT`. Der Abstand
`now() - pg_last_xact_replay_timestamp()` wächst weiter, wenn niemand
schreibt. Er misst dann nur die Zeit seit dem letzten Commit.

Dass die Zeile auf dem RO-Server sofort zu sehen war, garantiert nichts.
Auf `trainer-pg` lieferte eine Abfrage, die wenige Millisekunden nach dem
`COMMIT` auf dem RO-Server lief, noch den Stand davor.
CloudNativePG repliziert ohne weitere Konfiguration asynchron:
Der `COMMIT` auf der Primärinstanz wartet nicht auf das Replikat. Unter
Last kann eine gerade geschriebene Zeile auf `-ro` für kurze Zeit fehlen.
Ob dein Cluster synchron repliziert, zeigt `SHOW synchronous_standby_names`
auf dem RW-Server. Ein leerer Wert bedeutet asynchron.

Auch mit synchroner Replikation kann eine Zeile auf `-ro` kurz fehlen.
Mit `synchronous_commit = on` wartet der `COMMIT` nur, bis das Replikat das
WAL dauerhaft gespeichert hat. Eingespielt und für Abfragen sichtbar muss
die Zeile dann noch nicht sein. Darauf wartet erst
`synchronous_commit = remote_apply`.

`<cluster>-r` verteilt Verbindungen auf alle Instanzen einschließlich der
Primärinstanz. Ob eine Abfrage den neuesten Stand sieht, hängt dort von der
je Verbindung gewählten Instanz ab.

In der Pod-Liste aus Aufgabe 4 steht die Rolle in der Spalte
`INSTANCEROLE`, dem Label `cnpg.io/instanceRole`. Das ältere Label `role`
trägt denselben Wert, ist in CloudNativePG 1.30 aber als veraltet
markiert. Die Primärinstanz heißt nicht immer `<cluster>-1`: Nach einem
Switchover oder Failover übernimmt ein anderer Pod diese Rolle, und der
Dienst `<cluster>-rw` zeigt dann auf ihn.

`loesung.sql` entfernt zu Beginn `tickets.read_test` und lässt sich deshalb
mehrfach ausführen. Der ausführbare Teil läuft auf dem RW-Server. Die
Anweisungen für den RO-Server stehen als Kommentarblöcke mit der Markierung
`-- RO-Server`.
