-- Musterlösung zu SQL-Übung 8. Läuft vollständig im Query Tool und lässt
-- sich wiederholen: Der Rücksetzblock entfernt zu Beginn Materialized
-- View, View und den markierten Nachtrag. Aufgabe 4 braucht zwei
-- Verbindungen und steht deshalb als Kommentarblock, markiert mit
-- -- Verbindung A und -- Verbindung B.
SET search_path = tickets;
SET TimeZone = 'UTC';

DROP MATERIALIZED VIEW IF EXISTS tickets.team_report;
DROP VIEW IF EXISTS tickets.team_live;
DELETE FROM ticket WHERE metadata ? 'course_module08';

-- Aufgabe 1: View mit Tickets je Team und Monat
CREATE VIEW team_live AS
SELECT a.team, date_trunc('month', t.created_at)::date AS month, count(*) AS tickets_created
FROM ticket t
JOIN agent a ON a.id = t.agent_id
GROUP BY a.team, date_trunc('month', t.created_at);

-- Aufgabe 2: Materialized View mit derselben Abfrage, eindeutiger Index
-- als Voraussetzung für REFRESH ... CONCURRENTLY in Aufgabe 4
CREATE MATERIALIZED VIEW team_report AS
SELECT a.team, date_trunc('month', t.created_at)::date AS month, count(*) AS tickets_created
FROM ticket t
JOIN agent a ON a.id = t.agent_id
GROUP BY a.team, date_trunc('month', t.created_at);

CREATE UNIQUE INDEX team_report_team_month_idx ON team_report (team, month);

-- Aufgabe 3: Nachtrag einfügen, Abweichung zeigen (Ergebnis siehe AUFGABE.md)
SELECT (SELECT sum(tickets_created) FROM team_live) AS view_total,
       (SELECT sum(tickets_created) FROM team_report) AS matview_total;

INSERT INTO ticket (agent_id, subject, status, priority, metadata, created_at)
VALUES (1, 'Nachtrag fuer Teambericht', 'open', 1, '{"course_module08": true}', seed_base_date());

SELECT (SELECT sum(tickets_created) FROM team_live) AS view_total,
       (SELECT sum(tickets_created) FROM team_report) AS matview_total;

-- Aufgabe 4: REFRESH ... CONCURRENTLY neben einer offenen Lesetransaktion.
--
-- Verbindung A:
-- SET application_name = 'exercise_a';
-- BEGIN;
-- SELECT count(*) FROM tickets.team_report;
--
-- Verbindung B (während A offen ist):
-- SET application_name = 'exercise_b';
-- SET lock_timeout = '1s';
-- REFRESH MATERIALIZED VIEW tickets.team_report;
-- -- ERROR: 55P03: canceling statement due to lock timeout
--
-- Verbindung B (läuft trotz offener Transaktion von A durch):
-- REFRESH MATERIALIZED VIEW CONCURRENTLY tickets.team_report;
--
-- Verbindung A:
-- COMMIT;
--
-- Der ausführbare Teil bildet denselben Effekt in einer Verbindung nach.
REFRESH MATERIALIZED VIEW CONCURRENTLY team_report;

-- Kontrolle (Ergebnis siehe AUFGABE.md)
SELECT (SELECT sum(tickets_created) FROM team_live) AS view_total,
       (SELECT sum(tickets_created) FROM team_report) AS matview_total;
SELECT matviewname, ispopulated FROM pg_matviews WHERE schemaname = 'tickets';

RESET TimeZone;
