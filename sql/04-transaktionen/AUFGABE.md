# SQL-Übung 4: Transaktionen

## Ziel

Du bildest einen Lost Update auf einem Ticket nach und behebst ihn auf zwei
Arten: mit einer Zeilensperre und mit einem bedingten `UPDATE`. Danach
erzeugst du einen Schreibkonflikt unter `SERIALIZABLE`, findest eine
wartende Sitzung über den Systemkatalog und provozierst als Zusatzaufgabe
einen Deadlock. Jede erfolgreich abgeschlossene Zuweisung hältst du in
einer Protokolltabelle fest.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Du brauchst zwei
Verbindungen A und B, für Aufgabe 5 zusätzlich eine dritte Verbindung C.
Arbeite in allen drei mit `Auto commit` an und `Auto rollback on error`
aus. Aufgabe 4 lässt eine Transaktion bewusst mit einem Fehler enden, in
Aufgabe 1 und 3 bleiben Transaktionen absichtlich eine Weile offen.

Setze Ticket 1 und 2 zurück und lege die Protokolltabelle an:

```sql
UPDATE tickets.ticket SET agent_id = NULL WHERE id IN (1, 2);
DROP TABLE IF EXISTS tickets.assignment_log;
CREATE TABLE tickets.assignment_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ticket_id bigint NOT NULL,
    agent_id bigint NOT NULL,
    conn_label text NOT NULL
);
```

Setze in Verbindung A `SET application_name = 'exercise_a';` und in
Verbindung B `SET application_name = 'exercise_b';`. So findest du beide
Sitzungen gezielt in `pg_stat_activity`.

## Aufgaben

1. **Lost Update auf Ticket 1.** Beide Verbindungen lesen den freien
   Zustand, bevor eine von ihnen schreibt:

   In A:

   ```sql
   BEGIN;
   SELECT id, agent_id FROM tickets.ticket WHERE id = 1;
   ```

   In B, während die Transaktion von A noch offen ist:

   ```sql
   BEGIN;
   SELECT id, agent_id FROM tickets.ticket WHERE id = 1;
   ```

   Beide sehen `agent_id = NULL`. Weise nun in A Agent 1 zu und bestätige:

   ```sql
   UPDATE tickets.ticket SET agent_id = 1 WHERE id = 1;
   INSERT INTO tickets.assignment_log (ticket_id, agent_id, conn_label)
   VALUES (1, 1, 'A');
   COMMIT;
   ```

   Weise anschließend in B Agent 2 zu und bestätige, ohne den neuen Stand
   vorher erneut zu lesen:

   ```sql
   UPDATE tickets.ticket SET agent_id = 2 WHERE id = 1;
   INSERT INTO tickets.assignment_log (ticket_id, agent_id, conn_label)
   VALUES (1, 2, 'B');
   COMMIT;
   ```

   Beide `UPDATE`-Anweisungen laufen ohne Fehler durch. `tickets.ticket`
   enthält am Ende nur die Zuweisung von B. Die Zuweisung von A ist
   verloren, obwohl beide Sitzungen einen Erfolg protokolliert haben.

2. **Dieselbe Situation mit `SELECT ... FOR UPDATE`.** Setze Ticket 1 zuerst
   zurück: `UPDATE tickets.ticket SET agent_id = NULL WHERE id = 1;`

   In A:

   ```sql
   BEGIN;
   SELECT id, agent_id FROM tickets.ticket WHERE id = 1 FOR UPDATE;
   ```

   In B, während A die Transaktion offen hält:

   ```sql
   BEGIN;
   SELECT id, agent_id FROM tickets.ticket WHERE id = 1 FOR UPDATE;
   ```

   B wartet: Die Zeile ist durch A gesperrt. Schließe jetzt A ab:

   ```sql
   UPDATE tickets.ticket SET agent_id = 1 WHERE id = 1;
   INSERT INTO tickets.assignment_log (ticket_id, agent_id, conn_label)
   VALUES (1, 1, 'A');
   COMMIT;
   ```

   B erhält danach sein Ergebnis und sieht `agent_id = 1`, also die
   Zuweisung von A. B weist deshalb selbst nicht mehr zu:

   ```sql
   ROLLBACK;
   ```

3. **Bedingtes `UPDATE` auf Ticket 2.** Verbinde Bedingung und Änderung in
   einer Anweisung, ohne vorheriges `SELECT`. In A:

   ```sql
   BEGIN;
   UPDATE tickets.ticket SET agent_id = 1 WHERE id = 2 AND agent_id IS NULL;
   ```

   Die Meldung lautet `UPDATE 1`. Lasse die Transaktion offen und wechsle
   zu B:

   ```sql
   BEGIN;
   UPDATE tickets.ticket SET agent_id = 2 WHERE id = 2 AND agent_id IS NULL;
   ```

   B wartet, weil dieselbe Zeile durch die offene Änderung von A gesperrt
   ist. Schließe A ab:

   ```sql
   INSERT INTO tickets.assignment_log (ticket_id, agent_id, conn_label)
   VALUES (2, 1, 'A');
   COMMIT;
   ```

   B erhält danach die Meldung `UPDATE 0`: Die Bedingung `agent_id IS NULL`
   trifft auf die inzwischen von A geänderte Zeile nicht mehr zu. B trägt
   deshalb nichts in `tickets.assignment_log` ein und schließt ohne
   Änderung ab:

   ```sql
   ROLLBACK;
   ```

4. **Schreibkonflikt unter `SERIALIZABLE`.** Setze Ticket 1 zurück:
   `UPDATE tickets.ticket SET agent_id = NULL WHERE id = 1;`

   In A:

   ```sql
   BEGIN ISOLATION LEVEL SERIALIZABLE;
   SELECT id, agent_id FROM tickets.ticket WHERE id = 1;
   UPDATE tickets.ticket SET agent_id = 1 WHERE id = 1 AND agent_id IS NULL;
   ```

   Die Transaktion bleibt offen. In B, nachdem beide dieselbe
   Ausgangslage `agent_id = NULL` gelesen haben:

   ```sql
   BEGIN ISOLATION LEVEL SERIALIZABLE;
   SELECT id, agent_id FROM tickets.ticket WHERE id = 1;
   UPDATE tickets.ticket SET agent_id = 2 WHERE id = 1 AND agent_id IS NULL;
   ```

   B wartet auf die Zeilensperre von A. Schließe A ab:

   ```sql
   INSERT INTO tickets.assignment_log (ticket_id, agent_id, conn_label)
   VALUES (1, 1, 'A');
   COMMIT;
   ```

   Sobald A bestätigt, meldet die wartende Anweisung von B SQLSTATE
   `40001`: `could not serialize access due to concurrent update`. Unter
   `READ COMMITTED` würde PostgreSQL die Bedingung stillschweigend auf der
   neuen Zeilenversion wiederholen. Schließe B ab:

   ```sql
   ROLLBACK;
   ```

5. **Wartende Sitzung finden.** Wiederhole Schritt 4 bis zu dem Punkt, an
   dem B wartet, aber führe in A noch nicht den abschließenden `COMMIT`
   aus. Wechsle währenddessen zu einer dritten Verbindung C und führe aus:

   ```sql
   SELECT pid, application_name, state, wait_event_type, wait_event,
          pg_blocking_pids(pid) AS blockers
   FROM pg_stat_activity
   WHERE application_name IN ('exercise_a', 'exercise_b')
   ORDER BY application_name;
   ```

   B erscheint mit `wait_event_type = Lock`, `wait_event = transactionid`
   und der PID von A als Blockierer. Sieh dir zusätzlich die Sperren an:

   ```sql
   SELECT a.application_name, l.locktype, l.mode, l.granted
   FROM pg_locks l
   JOIN pg_stat_activity a ON a.pid = l.pid
   WHERE a.application_name IN ('exercise_a', 'exercise_b')
   ORDER BY a.application_name, l.mode;
   ```

   Für B steht dort eine nicht gewährte Sperre auf die Transaktions-ID von
   A (`granted = f`). Genau diese Zeile erklärt das Warten. Schließe danach
   in A mit `COMMIT;` und in B mit `ROLLBACK;` ab, wie in Aufgabe 4.

**Bonus: Deadlock.** Setze Ticket 1 und 2 zurück:
`UPDATE tickets.ticket SET agent_id = NULL WHERE id IN (1, 2);`

In A:

```sql
BEGIN;
SELECT id FROM tickets.ticket WHERE id = 1 FOR UPDATE;
```

In B:

```sql
BEGIN;
SELECT id FROM tickets.ticket WHERE id = 2 FOR UPDATE;
```

In A, danach:

```sql
SELECT id FROM tickets.ticket WHERE id = 2 FOR UPDATE;
```

A wartet auf B. Fordere jetzt in B die von A gehaltene Zeile an:

```sql
SELECT id FROM tickets.ticket WHERE id = 1 FOR UPDATE;
```

Eine der beiden Sitzungen bricht mit SQLSTATE `40P01` ab
(`deadlock detected`). Welche es trifft, ist nicht festgelegt. Die
abgebrochene Sitzung führt `ROLLBACK;` aus, die andere kann ihre
Transaktion regulär fortsetzen und abschließen.

## Ergebnis prüfen

```sql
SELECT ticket_id, count(*) AS assignments FROM tickets.assignment_log
GROUP BY ticket_id ORDER BY ticket_id;
SELECT id, agent_id FROM tickets.ticket WHERE id IN (1, 2) ORDER BY id;
```

Referenzlauf nach den Aufgaben 1 bis 4 in der beschriebenen Reihenfolge:

```text
 ticket_id | assignments 
-----------+-------------
         1 |           4
         2 |           1
(2 rows)

 id | agent_id 
----+----------
  1 |        1
  2 |        1
(2 rows)
```

Vier protokollierte Zuweisungen für Ticket 1 stammen aus Aufgabe 1 (A und
B, beide vermeintlich erfolgreich), Aufgabe 2 (nur A) und Aufgabe 4 (nur
A). Der tatsächliche Endstand von Ticket 1 zeigt trotzdem nur eine
einzige, zuletzt bestätigte Zuweisung. Der Bonus trägt nichts in
`tickets.assignment_log` ein, die erste Abfrage bleibt nach ihm also gleich.
Die zweite zeigt dann für Ticket 1 und 2 NULL in `agent_id`, weil der
Bonus beide Zuweisungen zurücksetzt.

Gib Ticket 1 und 2 zum Schluss ihre Agents aus `setup.sql` zurück. Die
Übungen 5 und 8 zählen Tickets je Team und rechnen mit dieser Zuordnung:

```sql
UPDATE tickets.ticket
SET agent_id = CASE id WHEN 1 THEN 1 WHEN 2 THEN 40 END
WHERE id IN (1, 2);
```

Die zweite Prüfabfrage zeigt danach `1 | 1` und `2 | 40`.

## Hinweise

Ein reines `SELECT` ohne `FOR UPDATE` verhindert kein gleichzeitiges
Schreiben. Genau das zeigt Aufgabe 1: Zwei Sitzungen entscheiden auf
Grundlage desselben gelesenen Ausgangszustands, ohne voneinander zu
wissen.

`SELECT ... FOR UPDATE` sperrt die gelesene Zeile bis zum Ende der eigenen
Transaktion. Eine wartende zweite Sitzung erhält nach der Freigabe den
inzwischen aktuellen Wert der Zeile. Darauf beruht die Prüfung in
Aufgabe 2.

Das bedingte `UPDATE` aus Aufgabe 3 braucht keine vorherige Sperre, weil
jedes `UPDATE` seine Zielzeile selbst sperrt. Eine zweite Sitzung mit
derselben Bedingung wartet automatisch und wertet die Bedingung nach der
Freigabe erneut aus.

Nach `40001` und `40P01` muss die betroffene Transaktion komplett von
vorn beginnen, mit allen Anweisungen. Das fehlgeschlagene `UPDATE` allein
zu wiederholen reicht nicht, wenn frühere Anweisungen auf demselben
veralteten Lesestand beruhten.

`pg_blocking_pids()` berücksichtigt Warteschlangen und ist deshalb einem
eigenen Selbstjoin auf `pg_locks` vorzuziehen. `pg_locks` allein zeigt
Sperrobjekte und ihren Modus, aber nicht immer eine vollständige Liste
aller Zeilensperren.

Die PID-Werte ändern sich von Lauf zu Lauf, das Verhalten bleibt gleich.
`loesung.sql` enthält die Anweisungen beider Verbindungen als
Kommentarblöcke, weil eine einzelne Skriptausführung keine zweite
Verbindung besitzt. Der ausführbare Teil setzt nur Ticket 1 und 2 zurück
und legt `tickets.assignment_log` neu an. Er lässt sich deshalb mehrfach
ausführen.
