-- Musterlösung zu SQL-Übung 5. Läuft vollständig im Query Tool und lässt
-- sich wiederholen: Der Rücksetzblock entfernt zu Beginn alle vier
-- Ergebnistabellen. ticket_metadata_gin (auch aus Übung 2) bleibt bestehen.
SET search_path = tickets;
SET TimeZone = 'UTC';

DROP TABLE IF EXISTS tickets.result_1;
DROP TABLE IF EXISTS tickets.result_2;
DROP TABLE IF EXISTS tickets.result_3;
DROP TABLE IF EXISTS tickets.result_4;

-- Aufgabe 1: Tickets je Team und Monat, kumuliert je Team
CREATE TABLE tickets.result_1 AS
WITH monthly_counts AS (
    SELECT a.team,
           date_trunc('month', t.created_at)::date AS month,
           count(*) AS tickets_created
    FROM ticket t
    JOIN agent a ON a.id = t.agent_id
    GROUP BY a.team, date_trunc('month', t.created_at)
)
SELECT team, month, tickets_created,
       sum(tickets_created) OVER (PARTITION BY team ORDER BY month) AS running_total
FROM monthly_counts;

-- Aufgabe 2: höchstens drei geschlossene Tickets je Agent
CREATE TABLE tickets.result_2 AS
SELECT agent_id, id AS ticket_id, closed_at, rank_no
FROM (
    SELECT agent_id, id, closed_at,
           row_number() OVER (PARTITION BY agent_id ORDER BY closed_at DESC, id DESC) AS rank_no
    FROM ticket
    WHERE status = 'closed' AND agent_id IS NOT NULL
) ranked
WHERE rank_no <= 3;

-- Aufgabe 3: Kommentarbaum von Ticket 9, rekursiv, Wurzel auf Tiefe 1
CREATE TABLE tickets.result_3 AS
WITH RECURSIVE tree AS (
    SELECT id AS comment_id, parent_id, author, 1 AS depth
    FROM comment
    WHERE ticket_id = 9 AND parent_id IS NULL

    UNION ALL

    SELECT c.id, c.parent_id, c.author, b.depth + 1
    FROM comment c
    JOIN tree b ON c.parent_id = b.comment_id
    WHERE c.ticket_id = 9
)
SELECT comment_id, parent_id, depth, author FROM tree;

-- Aufgabe 4: Tickets mit csat_score = 5 über einen SQL/JSON-Pfad
CREATE INDEX IF NOT EXISTS ticket_metadata_gin ON ticket USING gin (metadata);
ANALYZE ticket;

CREATE TABLE tickets.result_4 AS
SELECT id AS ticket_id, subject, (metadata->>'csat_score')::int AS csat_score
FROM ticket
WHERE metadata @? '$.csat_score ? (@ == 5)';

-- Aufgabe 5: jüngstes offenes Ticket je Team, ohne Ergebnistabelle
SELECT team.team, jt.ticket_id, jt.created_at
FROM (SELECT DISTINCT team FROM agent) team
CROSS JOIN LATERAL (
    SELECT t.id AS ticket_id, t.created_at
    FROM ticket t
    JOIN agent a ON a.id = t.agent_id
    WHERE a.team = team.team AND t.status = 'open'
    ORDER BY t.created_at DESC
    LIMIT 1
) jt
ORDER BY team.team;

-- Kontrolle (Ergebnis siehe AUFGABE.md)
SELECT team, count(*), max(running_total) FROM result_1 GROUP BY team ORDER BY team;
SELECT count(*), max(rank_no) FROM result_2;
SELECT depth, count(*) FROM result_3 GROUP BY depth ORDER BY depth;
SELECT count(*) FROM result_4;

RESET TimeZone;
