# SQL-Übung 14: Hochverfügbarkeit

## Ziel

Das Ticketsystem nimmt Aufträge wie "Ticket einer Agentin zuweisen"
entgegen, die eine Anwendung nach einem Verbindungsfehler wiederholen
kann. Du legst dafür eine Auftragstabelle mit einer Operations-ID an und
prüfst, dass eine Wiederholung keinen zweiten Auftrag erzeugt. Eine
Abfrage zeigt dir, wenn ein Schlüssel mit anderer Nutzlast wiederverwendet
wird. Danach beobachtest du, was eine offene Verbindung bei einem
Switchover erlebt, und entscheidest für typische Fehlerklassen, ob eine
Wiederholung sinnvoll ist.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool des RW-Servers deiner Servergruppe mit `Auto commit` an und
`Auto rollback on error` aus. Entferne zu Beginn die Übungstabelle:

```sql
DROP TABLE IF EXISTS tickets.operation;
```

Für Aufgabe 4 brauchst du ein zweites Query Tool auf dem RW-Server und
eines auf dem RO-Server. Ob du im eigenen Cluster einen Switchover
auslösen darfst, sagt dir die Kursleitung.

## Aufgaben

1. Lege die Auftragstabelle an:

   ```sql
   CREATE TABLE tickets.operation (
       operation_id uuid PRIMARY KEY,
       ticket_id bigint NOT NULL REFERENCES tickets.ticket (id),
       payload jsonb NOT NULL,
       executed_at timestamptz NOT NULL DEFAULT clock_timestamp()
   );
   ```

   Die Anwendung erzeugt `operation_id` einmal, bevor sie den Auftrag zum
   ersten Mal sendet. Bei jeder Wiederholung sendet sie dieselbe ID mit.

2. Führe denselben Auftrag zweimal aus:

   ```sql
   INSERT INTO tickets.operation (operation_id, ticket_id, payload)
   VALUES ('daf79a48-a152-47d4-9d92-3cca9782adf0', 1,
           '{"agent_id": 7}')
   ON CONFLICT (operation_id) DO NOTHING
   RETURNING operation_id;
   ```

   Die Nutzlast nennt die Agentin, der Ticket 1 zugewiesen wird. Notiere
   nach jedem Lauf, wie viele Zeilen `RETURNING` liefert.
   Wiederhole den Auftrag dann so, wie es eine Anwendung tut, die bei
   jedem Versuch eine neue ID erzeugt: dieselbe Anweisung mit
   `operation_id` `'4d45ac5a-e1a5-423c-ab40-38b15d424587'`. Zähle danach
   die Zeilen in `tickets.operation`. Wie viele Aufträge waren beabsichtigt?

3. Sende denselben Schlüssel mit einer anderen Nutzlast:

   ```sql
   INSERT INTO tickets.operation (operation_id, ticket_id, payload)
   VALUES ('daf79a48-a152-47d4-9d92-3cca9782adf0', 1,
           '{"agent_id": 12}')
   ON CONFLICT (operation_id) DO NOTHING
   RETURNING operation_id;
   ```

   Die Anweisung liefert keine Zeile und keinen Fehler. Schreibe eine
   Abfrage, die für diese `operation_id` die gespeicherte und die
   gesendete Nutzlast nebeneinander zeigt und in einer Spalte
   `same_payload` sagt, ob beide übereinstimmen.

4. Beobachte deine eigene Verbindung bei einem Switchover. Führe diese
   Abfrage im Query Tool des RW-Servers und im Query Tool des RO-Servers
   aus und notiere die Werte:

   ```sql
   SELECT inet_server_addr() AS server_addr,
          pg_postmaster_start_time() AS started_at,
          pg_is_in_recovery() AS is_replica,
          pg_backend_pid() AS pid;
   ```

   Die Kursleitung zeigt einen Switchover am Cluster `trainer-pg`. Wähle
   danach die Variante, die deine Rechte erlauben:

   - Mit dem Recht, im eigenen Cluster umzuschalten: Lies die Rollen,
     wähle ein Replikat und schalte um.

     ```bash
     kubectl -n training-postgres get pods -l cnpg.io/cluster=<cluster> -L role
     kubectl cnpg promote <cluster> <replikat-pod> -n training-postgres
     kubectl -n training-postgres get pods -l cnpg.io/cluster=<cluster> \
       -L role -w
     ```

     Ohne `kubectl` zeigt Headlamp die Rollen unter `Workloads`, `Pods`,
     Pod öffnen, Label `cnpg.io/instanceRole` mit `primary` oder
     `replica`. Welcher Weg in deiner Umgebung offen ist, sagt dir die
     Kursleitung. Das Umschalten selbst braucht `kubectl cnpg promote`.

     Führe die Abfrage danach in beiden Query Tools erneut aus, ohne neu
     zu verbinden. Notiere die Meldung im RW-Tab. Verbinde neu und
     vergleiche `server_addr` und `is_replica` mit den ersten Werten.

   - Ohne dieses Recht: Beende deine RW-Verbindung selbst. Setze im
     ersten Query Tool des RW-Servers zuerst
     `SET application_name = 'exercise14_a';`. Führe im zweiten Query Tool
     des RW-Servers aus:

     ```sql
     SELECT pid, pg_terminate_backend(pid) AS terminated
     FROM pg_stat_activity
     WHERE application_name = 'exercise14_a'
       AND usename = current_user;
     ```

     Führe die Beobachtungsabfrage danach im ersten Query Tool erneut aus
     und notiere die Meldung.

   Referenzlauf der zweiten Variante mit zwei psql-Sitzungen
   (PostgreSQL 18.6, lokal):

   ```text
    pid  | terminated 
   ------+------------
    6528 | t
   (1 row)

   FATAL:  57P01: terminating connection due to administrator command
   ```

5. Entscheide für jede Zeile, ob die Anwendung wiederholt, und wenn ja,
   was sie wiederholt:

   | SQLSTATE | Situation                                                 |
   | -------- | --------------------------------------------------------- |
   | `40001`  | Serializable-Konflikt oder lange Abfrage auf dem Replikat |
   | `40P01`  | Deadlock zwischen zwei Transaktionen                      |
   | `57P01`  | Server beendet die Verbindung, etwa beim Switchover       |
   | `57P03`  | Anmeldung, während der Server herunterfährt               |
   | `08006`  | Verbindung beim `COMMIT` verloren, auch ohne SQLSTATE     |
   | `23505`  | Doppelter Wert in einer eindeutigen Spalte                |
   | `23503`  | Auftrag für ein Ticket, das es nicht gibt                 |
   | `25006`  | `INSERT` über eine Verbindung zu `-ro`                    |
   | `42P01`  | Tabelle fehlt                                             |

## Ergebnis prüfen

```sql
SELECT operation_id, count(*) OVER () AS operations, payload
FROM tickets.operation
ORDER BY executed_at;
```

Referenzlauf mit psql (PostgreSQL 18.6, lokal) nach Aufgabe 3:

```text
             operation_id             | operations |     payload     
--------------------------------------+------------+-----------------
 daf79a48-a152-47d4-9d92-3cca9782adf0 |          2 | {"agent_id": 7}
 4d45ac5a-e1a5-423c-ab40-38b15d424587 |          2 | {"agent_id": 7}
(2 rows)
```

Die erste Zeile stammt aus Aufgabe 2 und bleibt unverändert, weil weder
die Wiederholung mit derselben ID noch der Versuch mit anderer Nutzlast aus
Aufgabe 3 eine Zeile anlegt. Die zweite Zeile stammt aus der
Wiederholung mit neuer ID. Beabsichtigt war ein Auftrag.

Die Abfrage aus Aufgabe 3 in `loesung.sql` zeigte im selben Lauf:

```text
     stored      |       sent       | same_payload 
-----------------+------------------+--------------
 {"agent_id": 7} | {"agent_id": 12} | f
(1 row)
```

Steht nur eine Zeile in `tickets.operation`, fehlt die Wiederholung mit
neuer ID aus Aufgabe 2. Meldet die Abfrage `42P01`, fehlt Aufgabe 1.

## Hinweise

`ON CONFLICT (operation_id) DO NOTHING` fängt nur Konflikte auf dem
Primärschlüssel ab. Ohne die Angabe `(operation_id)` würde die Anweisung
auch Konflikte auf jeder anderen eindeutigen Spalte still übergehen.

`RETURNING` liefert beim ersten Versuch eine Zeile und bei jeder
Wiederholung keine. Daran erkennt die Anwendung einen Auftrag, der schon
ausgeführt ist. Danach vergleicht sie die gespeicherte Nutzlast mit der
gesendeten. Stimmen sie überein, meldet sie Erfolg. Weichen sie ab, ist
es ein Programmfehler, und eine Wiederholung ändert daran nichts. `jsonb`
vergleicht den Inhalt; Leerzeichen und die Reihenfolge der Schlüssel sind
dabei egal.

Die fachliche Wirkung eines Auftrags gehört in dieselbe Transaktion wie
die Zeile in `tickets.operation`. Dann stehen nach einem Abbruch entweder
beide in der Datenbank oder keines von beiden. Eine Anweisung mit
datenverändernder CTE erledigt das in einem Schritt. Das `UPDATE` weist
das Ticket nur zu, wenn der Auftrag neu ist; in der Anwendung stehen `$1`
bis `$3` für die Parameter:

```sql
WITH inserted AS (
    INSERT INTO tickets.operation (operation_id, ticket_id, payload)
    VALUES ($1, $2, $3)
    ON CONFLICT (operation_id) DO NOTHING
    RETURNING ticket_id, payload
)
UPDATE tickets.ticket AS t
SET agent_id = (inserted.payload ->> 'agent_id')::bigint
FROM inserted
WHERE t.id = inserted.ticket_id;
```

Ein lokaler Lauf mit festen Werten für Ticket 2 meldete zuerst
`UPDATE 1` und bei der Wiederholung mit derselben ID und anderer Agentin
`UPDATE 0`. Ticket 2 behielt die Agentin aus dem ersten Lauf.

Beim Switchover fährt die alte Primärinstanz schnell herunter. Sie rollt
offene Transaktionen zurück und beendet jede Verbindung mit `57P01`,
derselben Meldung wie `pg_terminate_backend`. `57P01` kommt als `FATAL`,
deshalb braucht das Query Tool für weitere Befehle eine neue Verbindung. Der
Hostname `<cluster>-rw` führt danach zur neuen Primärinstanz.

Eine Verbindung zum RO-Server kann den Switchover überstehen, wenn sie mit
dem Replikat verbunden war, das zur Primärinstanz wird. `is_replica` zeigt
dann `f` auf derselben Verbindung. Prüfe deshalb nach einem Switchover mit
`pg_is_in_recovery()`, wohin eine offene Verbindung zeigt.

`40001` kommt auch auf einem Replikat vor: Eine lange Abfrage bricht mit
`canceling statement due to conflict with recovery` ab, wenn das
Einspielen von WAL Zeilen entfernt, die sie noch braucht.

Npgsql markiert `57P01`, `57P03`, `40001`, `40P01` und Netzwerkfehler mit
`IsTransient = true`. Das sagt, dass ein neuer Versuch gelingen kann. Ob
er eine Wirkung verdoppelt, entscheidet die Operations-ID. Die .NET-Übung
06 im Ordner `uebungen/06-betrieb` zeigt `Keepalive` und das Lesen vom
Replikat.

`loesung.sql` entfernt zu Beginn `tickets.operation` und lässt sich deshalb
mehrfach ausführen. Aufgabe 4 steht dort als Kommentarblock, weil sie
mehrere Verbindungen und einen Switchover braucht. Die Entscheidungen zu
Aufgabe 5 stehen als Kommentar am Ende.
