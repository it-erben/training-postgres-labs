-- Musterlösung zu SQL-Übung 5. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Am Stück zeigt Data Output nur die letzte Kontrollabfrage.
-- Der erste Abschnitt entfernt alle vier Ergebnistabellen, die Datei lässt
-- sich deshalb wiederholen. ticket_metadata_gin (auch aus Übung 2) bleibt
-- bestehen.

-- Abschnitt 1: Ergebnistabellen entfernen
DROP TABLE IF EXISTS tickets.result_1, tickets.result_2,
                     tickets.result_3, tickets.result_4;

-- Abschnitt 2 (Aufgabe 1): Tickets je Team und Monat, kumuliert je Team.
-- AT TIME ZONE 'UTC' legt die Monatsgrenzen unabhängig von der
-- Sitzungszeitzone fest.
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

-- Abschnitt 3 (Aufgabe 2): höchstens drei geschlossene Tickets je Agent
CREATE TABLE tickets.result_2 AS
SELECT agent_id, id AS ticket_id, closed_at, rank_no
FROM (
    SELECT agent_id, id, closed_at,
           row_number() OVER (PARTITION BY agent_id ORDER BY closed_at DESC, id DESC) AS rank_no
    FROM tickets.ticket
    WHERE status = 'closed' AND agent_id IS NOT NULL
) ranked
WHERE rank_no <= 3;

-- Abschnitt 4 (Aufgabe 3): Kommentarbaum von Ticket 9, rekursiv, Wurzel
-- auf Tiefe 1
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

-- Abschnitt 5 (Aufgabe 4): Tickets mit csat_score = 5 über einen
-- SQL/JSON-Pfad
CREATE INDEX IF NOT EXISTS ticket_metadata_gin
    ON tickets.ticket USING gin (metadata);
ANALYZE tickets.ticket;

CREATE TABLE tickets.result_4 AS
SELECT id AS ticket_id, subject, (metadata->>'csat_score')::int AS csat_score
FROM tickets.ticket
WHERE metadata @? '$.csat_score ? (@ == 5)';

-- Abschnitt 6 (Aufgabe 5): jüngstes offenes Ticket je Team, ohne
-- Ergebnistabelle
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

-- Abschnitt 7: Kontrolle result_1
SELECT team, count(*), max(running_total) FROM tickets.result_1
GROUP BY team ORDER BY team;

-- Abschnitt 8: Kontrolle result_2
SELECT count(*), max(rank_no) FROM tickets.result_2;

-- Abschnitt 9: Kontrolle result_3
SELECT depth, count(*) FROM tickets.result_3 GROUP BY depth ORDER BY depth;

-- Abschnitt 10: Kontrolle result_4
SELECT count(*) FROM tickets.result_4;
