# SQL-Übung 5: Moderne SQL-Features

## Ziel

Du beantwortest vier Fragen an das Ticketsystem mit Fensterfunktionen,
einer rekursiven CTE und einem SQL/JSON-Pfadausdruck. Jede Antwort landet
als eigene Tabelle unter `tickets`. Zum Schluss liest du mit
`CROSS JOIN LATERAL` je Team das jüngste offene Ticket.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus.

Entferne zu Beginn die vier Ergebnistabellen, damit sich die Übung
wiederholen lässt:

```sql
DROP TABLE IF EXISTS tickets.ergebnis_1;
DROP TABLE IF EXISTS tickets.ergebnis_2;
DROP TABLE IF EXISTS tickets.ergebnis_3;
DROP TABLE IF EXISTS tickets.ergebnis_4;
```

## Aufgaben

1. Erzeuge `tickets.ergebnis_1(team, monat, tickets_erstellt, laufende_summe)`:
   Tickets je Team und Monat zählen, danach je Team über die Monate
   kumulieren.

   ```sql
   SET TimeZone = 'UTC';
   CREATE TABLE tickets.ergebnis_1 AS
   WITH monatswerte AS (
       SELECT a.team,
              date_trunc('month', t.created_at)::date AS monat,
              count(*) AS tickets_erstellt
       FROM tickets.ticket t
       JOIN tickets.agent a ON a.id = t.agent_id
       GROUP BY a.team, date_trunc('month', t.created_at)
   )
   SELECT team, monat, tickets_erstellt,
          sum(tickets_erstellt) OVER (PARTITION BY team ORDER BY monat) AS laufende_summe
   FROM monatswerte;
   ```

   Der innere `JOIN` verbindet nur zugeordnete Tickets mit ihrem Agenten und
   damit ihrem Team. Ein Ticket ohne `agent_id` fließt nicht ein.
   `date_trunc('month', ...)` rundet den Zeitpunkt auf den Monatsanfang. Die
   Fensterfunktion `sum() OVER (PARTITION BY team ORDER BY monat)` läuft
   ohne eigene Rahmenklausel und summiert deshalb per Voreinstellung von der
   ersten Zeile der Partition bis zur aktuellen Zeile.

2. Erzeuge `tickets.ergebnis_2(agent_id, ticket_id, closed_at, rang)` mit
   höchstens drei geschlossenen Tickets je Agent, sortiert nach
   `closed_at DESC, id DESC`:

   ```sql
   CREATE TABLE tickets.ergebnis_2 AS
   SELECT agent_id, id AS ticket_id, closed_at, rang
   FROM (
       SELECT agent_id, id, closed_at,
              row_number() OVER (
                  PARTITION BY agent_id ORDER BY closed_at DESC, id DESC
              ) AS rang
       FROM tickets.ticket
       WHERE status = 'closed' AND agent_id IS NOT NULL
   ) eingeordnet
   WHERE rang <= 3;
   ```

   Mehrere hundert Tickets je Agent teilen sich denselben letzten
   `closed_at`-Wert, weil `setup.sql` das Kursende als obere Schranke für
   alle Abschlusszeiten verwendet. Mit `id DESC` als zweitem
   Sortierkriterium ist die Rangfolge auch bei gleicher Zeit eindeutig.

3. Erzeuge `tickets.ergebnis_3(comment_id, parent_id, tiefe, author)`
   rekursiv aus dem Kommentarbaum von Ticket 9. Die Wurzel erhält Tiefe 1:

   ```sql
   CREATE TABLE tickets.ergebnis_3 AS
   WITH RECURSIVE baum AS (
       SELECT id AS comment_id, parent_id, author, 1 AS tiefe
       FROM tickets.comment
       WHERE ticket_id = 9 AND parent_id IS NULL

       UNION ALL

       SELECT c.id, c.parent_id, c.author, b.tiefe + 1
       FROM tickets.comment c
       JOIN baum b ON c.parent_id = b.comment_id
       WHERE c.ticket_id = 9
   )
   SELECT comment_id, parent_id, tiefe, author FROM baum;
   ```

   Der nicht rekursive Teil liefert die Wurzel des Baums, der rekursive Teil
   hängt je Durchlauf die nächste Ebene an. `WHERE c.ticket_id = 9` in
   beiden Teilen verhindert, dass ein Kommentar eines anderen Tickets über
   eine zufällig gleiche `parent_id` einfließt.

4. Erzeuge `tickets.ergebnis_4(ticket_id, subject, csat_score)` mit einem
   SQL/JSON-Pfadausdruck und lege dafür einen GIN-Index auf `metadata` an:

   ```sql
   CREATE INDEX IF NOT EXISTS ticket_metadata_gin
       ON tickets.ticket USING gin (metadata);
   ANALYZE tickets.ticket;

   CREATE TABLE tickets.ergebnis_4 AS
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

   Referenzlauf:

   ```text
       team    | ticket_id |          created_at           
   ------------+-----------+-------------------------------
    Billing    |    700319 | 2026-08-20 21:44:36.572929+00
    Onboarding |    521500 | 2026-08-20 16:26:50.518429+00
    Retention  |    651522 | 2026-08-20 08:39:32.502735+00
    Technik    |     54137 | 2026-08-20 16:43:39.751538+00
   (4 rows)
   ```

   `team` in der äußeren Ableitung liefert die vier vorhandenen Teams ohne
   Duplikate. Der `LATERAL`-Teil greift auf die Spalte `team.team` der
   äußeren Zeile zu. Mit einem gewöhnlichen `JOIN` geht das nicht, weil die
   Unterabfrage dann vor jedem Zugriff auf die äußere Zeile feststehen
   müsste. Da `LIMIT 1` für jedes Team einzeln greift, entsteht genau eine
   Ergebniszeile pro Team.

## Ergebnis prüfen

```sql
SET TimeZone = 'UTC';
SELECT team, count(*), max(laufende_summe) FROM tickets.ergebnis_1
GROUP BY team ORDER BY team;
SELECT count(*), max(rang) FROM tickets.ergebnis_2;
SELECT tiefe, count(*) FROM tickets.ergebnis_3 GROUP BY tiefe ORDER BY tiefe;
SELECT count(*) FROM tickets.ergebnis_4;
RESET TimeZone;
```

Referenzlauf:

```text
    team    | count |  max   
------------+-------+--------
 Billing    |    25 | 273940
 Onboarding |    25 | 114676
 Retention  |    25 | 100992
 Technik    |    25 | 230551
(4 rows)

 count | max 
-------+-----
   150 |   3
(1 row)

 tiefe | count 
-------+-------
     1 |     1
     2 |     2
     3 |     1
(3 rows)

 count 
-------
 80307
(1 row)
```

Jedes der vier Teams verteilt sich auf 25 Monate, denn `created_at` deckt
730 Tage vor dem Kursstichtag ab. `ergebnis_2` enthält 150 Zeilen, also 50
Agenten mit höchstens drei Zeilen. `max(rang) = 3` bestätigt, dass die
Begrenzung greift. Der Kommentarbaum von Ticket 9 hat eine Wurzel, zwei
Kommentare auf Tiefe 2 und einen auf Tiefe 3.

## Hinweise

Die laufende Summe in `ergebnis_1` bezieht sich je Zeile nur auf das eigene
Team, weil die Fensterfunktion danach partitioniert. Ein `ORDER BY` ohne
`PARTITION BY` würde über alle Teams hinweg kumulieren.

In der inneren Ebene von `ergebnis_2`, neben `WHERE status = 'closed'`,
gibt es `rang` noch nicht. Deshalb filtert erst die äußere Abfrage auf
`rang <= 3`. Eine `WHERE`-Bedingung auf ein Fensterfunktionsergebnis
braucht immer eine äußere Abfrage oder eine CTE.

Eine rekursive CTE ohne Filter auf `ticket_id` innerhalb des rekursiven
Teils würde bei einer `id`-Kollision zwischen `parent_id`-Werten
verschiedener Tickets falsche Zweige aufnehmen. In diesem Datenbestand ist
`comment.id` global eindeutig. Die zusätzliche Bedingung bleibt trotzdem
die verlässlichere Formulierung.

`loesung.sql` entfernt zu Beginn `tickets.ergebnis_1` bis `ergebnis_4` und
lässt sich deshalb mehrfach ausführen. `ticket_metadata_gin` bleibt
bestehen, weil mehrere Übungen den Index nutzen.
