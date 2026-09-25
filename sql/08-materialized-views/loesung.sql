-- Musterlösung zu SQL-Übung 8. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Abschnitt 1 entfernt Materialized View, View und den
-- markierten Nachtrag, die Datei lässt sich deshalb wiederholen. Aufgabe 4
-- braucht zwei Verbindungen und steht als Kommentarblock, markiert mit
-- -- Verbindung A und -- Verbindung B.

-- Abschnitt 1: Übungsobjekte und Nachtrag entfernen
DROP MATERIALIZED VIEW IF EXISTS tickets.team_report;
DROP VIEW IF EXISTS tickets.team_live;
DELETE FROM tickets.ticket WHERE metadata ? 'course_module08';

-- Abschnitt 2 (Aufgabe 1): View mit Tickets je Team und Monat. AT TIME
-- ZONE 'UTC' legt die Monatsgrenzen unabhängig von der Sitzungszeitzone
-- fest.
CREATE VIEW tickets.team_live AS
SELECT a.team,
       date_trunc('month', t.created_at AT TIME ZONE 'UTC')::date AS month,
       count(*) AS tickets_created
FROM tickets.ticket t
JOIN tickets.agent a ON a.id = t.agent_id
GROUP BY a.team, date_trunc('month', t.created_at AT TIME ZONE 'UTC');

-- Abschnitt 3 (Aufgabe 2): Materialized View mit derselben Abfrage,
-- eindeutiger Index als Voraussetzung für REFRESH ... CONCURRENTLY
CREATE MATERIALIZED VIEW tickets.team_report AS
SELECT a.team,
       date_trunc('month', t.created_at AT TIME ZONE 'UTC')::date AS month,
       count(*) AS tickets_created
FROM tickets.ticket t
JOIN tickets.agent a ON a.id = t.agent_id
GROUP BY a.team, date_trunc('month', t.created_at AT TIME ZONE 'UTC');

CREATE UNIQUE INDEX team_report_team_month_idx
    ON tickets.team_report (team, month);

-- Abschnitt 4 (Aufgabe 3): Summen vor dem Nachtrag, 720159 und 720159
SELECT (SELECT sum(tickets_created) FROM tickets.team_live) AS view_total,
       (SELECT sum(tickets_created) FROM tickets.team_report) AS matview_total;

-- Abschnitt 5 (Aufgabe 3): Nachtrag
INSERT INTO tickets.ticket (agent_id, subject, status, priority, metadata, created_at)
VALUES (1, 'Nachtrag fuer Teambericht', 'open', 1, '{"course_module08": true}',
        tickets.seed_base_date());

-- Abschnitt 6 (Aufgabe 3): Summen nach dem Nachtrag, 720160 und 720159
SELECT (SELECT sum(tickets_created) FROM tickets.team_live) AS view_total,
       (SELECT sum(tickets_created) FROM tickets.team_report) AS matview_total;

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

-- Abschnitt 7 (Aufgabe 4): Der ausführbare Teil aktualisiert in einer
-- Verbindung.
REFRESH MATERIALIZED VIEW CONCURRENTLY tickets.team_report;

-- Abschnitt 8: Kontrolle der Summen
SELECT (SELECT sum(tickets_created) FROM tickets.team_live) AS view_total,
       (SELECT sum(tickets_created) FROM tickets.team_report) AS matview_total;

-- Abschnitt 9: Kontrolle der Materialized View
SELECT matviewname, ispopulated FROM pg_matviews WHERE schemaname = 'tickets';
