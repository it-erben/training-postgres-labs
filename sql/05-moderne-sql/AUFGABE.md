# SQL-Übung 5: Moderne SQL-Features

## Ziel

Du beantwortest vier Fragen an das Ticketsystem mit Fensterfunktionen,
einer rekursiven CTE und einem SQL/JSON-Pfadausdruck. Jede Antwort landet
als eigene Tabelle unter `tickets`. Zum Schluss liest du mit
`CROSS JOIN LATERAL` je Team das jüngste offene Ticket.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus. Jeder Codeblock
ist eine Ausführung, wie in Übung 0 beschrieben.

Entferne zu Beginn die vier Ergebnistabellen, damit sich die Übung
wiederholen lässt:

```sql
DROP TABLE IF EXISTS tickets.result_1, tickets.result_2,
                     tickets.result_3, tickets.result_4;
```

## Aufgaben

1. Erzeuge `tickets.result_1(team, month, tickets_created, running_total)`:
   Tickets je Team und Monat zählen, danach je Team über die Monate
   kumulieren.

   ```sql
   CREATE TABLE tickets.result_1 AS
   WITH monthly_counts AS (
       SELECT a.team,
              date_trunc('month', t.created_at AT TIME ZONE 'UTC')::date
                  AS month,
              count(*) AS tickets_created
       FROM tickets.ticket t
       JOIN tickets.agent a ON a.id = t.agent_id
       GROUP BY a.team, date_trunc('month', t.created_at AT TIME ZONE 'UTC')
   )
   SELECT team, month, tickets_created,
          sum(tickets_created) OVER (PARTITION BY team ORDER BY month) AS running_total
   FROM monthly_counts;
   ```

   Der innere `JOIN` verbindet nur zugeordnete Tickets mit ihrem Agenten und
   damit ihrem Team. Ein Ticket ohne `agent_id` fließt nicht ein.
   `created_at AT TIME ZONE 'UTC'` liefert die Uhrzeit in UTC, damit liegen
   die Monatsgrenzen unabhängig von der Sitzungszeitzone fest.
   `date_trunc('month', ...)` rundet den Zeitpunkt auf den Monatsanfang. Die
   Fensterfunktion `sum() OVER (PARTITION BY team ORDER BY month)` läuft
   ohne eigene Rahmenklausel. Mit `ORDER BY` gilt dann der Rahmen
   `RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW`: Er reicht von der
   ersten Zeile der Partition bis zur letzten Zeile mit demselben `month`
   wie die aktuelle Zeile. Weil `month` je Team nur einmal vorkommt, endet
   die Summe hier genau an der aktuellen Zeile.

2. Erzeuge `tickets.result_2(agent_id, ticket_id, closed_at, rank_no)` mit
   höchstens drei geschlossenen Tickets je Agent, sortiert nach
   `closed_at DESC, id DESC`:

   ```sql
   CREATE TABLE tickets.result_2 AS
   SELECT agent_id, id AS ticket_id, closed_at, rank_no
   FROM (
       SELECT agent_id, id, closed_at,
              row_number() OVER (
                  PARTITION BY agent_id ORDER BY closed_at DESC, id DESC
              ) AS rank_no
       FROM tickets.ticket
       WHERE status = 'closed' AND agent_id IS NOT NULL
   ) ranked
   WHERE rank_no <= 3;
   ```

   Mehrere hundert Tickets je Agent teilen sich denselben letzten
   `closed_at`-Wert. `setup.sql` begrenzt alle Abschlusszeiten nach oben
   auf `seed_base_date()`, also 2026-08-21 00:00 UTC. Mit `id DESC` als
   zweitem Sortierkriterium ist die Rangfolge auch bei gleicher Zeit
   eindeutig.

3. Erzeuge `tickets.result_3(comment_id, parent_id, depth, author)`
   rekursiv aus dem Kommentarbaum von Ticket 9. Die Wurzel erhält Tiefe 1:

   ```sql
   CREATE TABLE tickets.result_3 AS
   WITH RECURSIVE tree AS (
       SELECT id AS comment_id, parent_id, author, 1 AS depth
       FROM tickets.comment
       WHERE ticket_id = 9 AND parent_id IS NULL

       UNION ALL

       SELECT c.id, c.parent_id, c.author, b.depth + 1
       FROM tickets.comment c
       JOIN tree b ON c.parent_id = b.comment_id
       WHERE c.ticket_id = 9
   )
   SELECT comment_id, parent_id, depth, author FROM tree;
   ```

   Der nicht rekursive Teil liefert die Wurzel des Baums, der rekursive Teil
   hängt je Durchlauf die nächste Ebene an. Der Fremdschlüssel auf
   `comment(id)` verlangt nicht, dass eine Antwort zum selben Ticket gehört
   wie ihr Elternkommentar. `WHERE c.ticket_id = 9` im rekursiven Teil
   schließt solche Antworten aus anderen Tickets aus.

4. Erzeuge `tickets.result_4(ticket_id, subject, csat_score)` mit einem
   SQL/JSON-Pfadausdruck und lege dafür einen GIN-Index auf `metadata` an.
   Der Block läuft als eine Ausführung:

   ```sql
   CREATE INDEX IF NOT EXISTS ticket_metadata_gin
       ON tickets.ticket USING gin (metadata);
   ANALYZE tickets.ticket;

   CREATE TABLE tickets.result_4 AS
   SELECT id AS ticket_id, subject, (metadata->>'csat_score')::int AS csat_score
   FROM tickets.ticket
   WHERE metadata @? '$.csat_score ? (@ == 5)';
   ```

   `ticket_metadata_gin` kann bereits aus [Übung 2](../02-explain/AUFGABE.md)
   vorhanden sein. `CREATE INDEX IF NOT EXISTS` legt ihn nur einmal an.
   `@?` prüft, ob der JSON-Pfad mindestens ein Ergebnis liefert. Der Filter
   `? (@ == 5)` innerhalb des Pfades trifft nur Tickets mit `csat_score = 5`.

5. Lies mit `CROSS JOIN LATERAL` je Team das jüngste offene Ticket:

   ```sql
   SELECT team.team, jt.ticket_id, jt.created_at
   FROM (SELECT DISTINCT team FROM tickets.agent) team
   CROSS JOIN LATERAL (
       SELECT t.id AS ticket_id, t.created_at
       FROM tickets.ticket t
       JOIN tickets.agent a ON a.id = t.agent_id
       WHERE a.team = team.team AND t.status = 'open'
       ORDER BY t.created_at DESC
       LIMIT 1
   ) jt
   ORDER BY team.team;
   ```

   Referenzlauf. `created_at` erscheint in der Zeitzone der Sitzung, auf dem
   Kurscluster UTC:

   ```text
       team    | ticket_id |          created_at           
   ------------+-----------+-------------------------------
    Billing    |    700319 | 2026-08-20 21:44:36.572929+00
    Onboarding |    521500 | 2026-08-20 16:26:50.518429+00
    Retention  |    651522 | 2026-08-20 08:39:32.502735+00
    Technical  |     54137 | 2026-08-20 16:43:39.751538+00
   (4 rows)
   ```

   `team` in der äußeren Ableitung liefert die vier vorhandenen Teams ohne
   Duplikate. Der `LATERAL`-Teil greift auf die Spalte `team.team` der
   äußeren Zeile zu. Mit einem gewöhnlichen `JOIN` geht das nicht, weil die
   Unterabfrage dann vor jedem Zugriff auf die äußere Zeile feststehen
   müsste. Da `LIMIT 1` für jedes Team einzeln greift, entsteht höchstens
   eine Ergebniszeile pro Team. Ein Team ohne offenes Ticket fehlt ganz,
   weil `CROSS JOIN LATERAL` bei leerer Unterabfrage keine Zeile liefert.
   `LEFT JOIN LATERAL (...) jt ON true` behält es mit NULL-Werten.

## Ergebnis prüfen

Vier Abfragen, jede als eigener Block. Monate und laufende Summe je Team:

```sql
SELECT team, count(*), max(running_total) FROM tickets.result_1
GROUP BY team ORDER BY team;
```

```text
    team    | count |  max
------------+-------+--------
 Billing    |    25 | 273940
 Onboarding |    25 | 114676
 Retention  |    25 | 100992
 Technical  |    25 | 230551
(4 rows)
```

Die Rangliste der geschlossenen Tickets:

```sql
SELECT count(*), max(rank_no) FROM tickets.result_2;
```

```text
 count | max
-------+-----
   150 |   3
(1 row)
```

Der Kommentarbaum von Ticket 9:

```sql
SELECT depth, count(*) FROM tickets.result_3 GROUP BY depth ORDER BY depth;
```

```text
 depth | count
-------+-------
     1 |     1
     2 |     2
     3 |     1
(3 rows)
```

Die Tickets mit `csat_score = 5`:

```sql
SELECT count(*) FROM tickets.result_4;
```

```text
 count
-------
 80307
(1 row)
```

Jedes der vier Teams verteilt sich auf 25 Monate, denn `created_at` deckt
730 Tage vor `seed_base_date()` ab. `result_2` enthält 150 Zeilen, also 50
Agenten mit höchstens drei Zeilen. `max(rank_no) = 3` bestätigt, dass die
Begrenzung greift. Der Kommentarbaum von Ticket 9 hat eine Wurzel, zwei
Kommentare auf Tiefe 2 und einen auf Tiefe 3.

## Hinweise

Die laufende Summe in `result_1` bezieht sich je Zeile nur auf das eigene
Team, weil die Fensterfunktion danach partitioniert. Ein `ORDER BY` ohne
`PARTITION BY` würde über alle Teams hinweg kumulieren.

In der inneren Ebene von `result_2`, neben `WHERE status = 'closed'`,
gibt es `rank_no` noch nicht. Deshalb filtert erst die äußere Abfrage auf
`rank_no <= 3`. Eine `WHERE`-Bedingung auf ein Fensterfunktionsergebnis
braucht immer eine äußere Abfrage oder eine CTE.

`setup.sql` hängt jede Antwort an einen Kommentar desselben Tickets. Im
Datenbestand ändert der Filter auf `ticket_id` im rekursiven Teil das
Ergebnis deshalb nicht. Er hält die Abfrage richtig, wenn später eine
Antwort auf einen Kommentar eines anderen Tickets verweist; das Schema
verbietet das nicht.

`loesung.sql` entfernt zu Beginn `tickets.result_1` bis `result_4` und
lässt sich deshalb mehrfach ausführen. `ticket_metadata_gin` bleibt
bestehen, weil mehrere Übungen den Index nutzen.
