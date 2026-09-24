-- Musterlösung zu SQL-Übung 8. Läuft vollständig im Query Tool und lässt
-- sich wiederholen: Der Rücksetzblock entfernt zu Beginn Materialized
-- View, View und den markierten Nachtrag. Aufgabe 4 braucht zwei
-- Verbindungen und steht deshalb als Kommentarblock, markiert mit
-- -- Verbindung A und -- Verbindung B.
SET search_path = tickets;
SET TimeZone = 'UTC';

DROP MATERIALIZED VIEW IF EXISTS tickets.team_bericht;
DROP VIEW IF EXISTS tickets.team_aktuell;
DELETE FROM ticket WHERE metadata ? 'kurs_modul08';

-- Aufgabe 1: View mit Tickets je Team und Monat
CREATE VIEW team_aktuell AS
SELECT a.team, date_trunc('month', t.created_at)::date AS monat, count(*) AS tickets_erstellt
FROM ticket t
JOIN agent a ON a.id = t.agent_id
GROUP BY a.team, date_trunc('month', t.created_at);

-- Aufgabe 2: Materialized View mit derselben Abfrage, eindeutiger Index
-- als Voraussetzung für REFRESH ... CONCURRENTLY in Aufgabe 4
CREATE MATERIALIZED VIEW team_bericht AS
SELECT a.team, date_trunc('month', t.created_at)::date AS monat, count(*) AS tickets_erstellt
FROM ticket t
JOIN agent a ON a.id = t.agent_id
GROUP BY a.team, date_trunc('month', t.created_at);

CREATE UNIQUE INDEX team_bericht_team_monat_idx ON team_bericht (team, monat);

-- Aufgabe 3: Nachtrag einfügen, Abweichung zeigen (Ergebnis siehe AUFGABE.md)
SELECT (SELECT sum(tickets_erstellt) FROM team_aktuell) AS view_summe,
       (SELECT sum(tickets_erstellt) FROM team_bericht) AS matview_summe;

INSERT INTO ticket (agent_id, subject, status, priority, metadata, created_at)
VALUES (1, 'Nachtrag fuer Teambericht', 'open', 1, '{"kurs_modul08": true}', seed_base_date());

SELECT (SELECT sum(tickets_erstellt) FROM team_aktuell) AS view_summe,
       (SELECT sum(tickets_erstellt) FROM team_bericht) AS matview_summe;

-- Aufgabe 4: REFRESH ... CONCURRENTLY neben einer offenen Lesetransaktion.
--
-- Verbindung A:
-- SET application_name = 'uebung_a';
-- BEGIN;
-- SELECT count(*) FROM tickets.team_bericht;
--
-- Verbindung B (während A offen ist):
-- SET application_name = 'uebung_b';
-- SET lock_timeout = '1s';
-- REFRESH MATERIALIZED VIEW tickets.team_bericht;
-- -- ERROR: 55P03: canceling statement due to lock timeout
--
-- Verbindung B (läuft trotz offener Transaktion von A durch):
-- REFRESH MATERIALIZED VIEW CONCURRENTLY tickets.team_bericht;
--
-- Verbindung A:
-- COMMIT;
--
-- Der ausführbare Teil bildet denselben Effekt in einer Verbindung nach.
REFRESH MATERIALIZED VIEW CONCURRENTLY team_bericht;

-- Kontrolle (Ergebnis siehe AUFGABE.md)
SELECT (SELECT sum(tickets_erstellt) FROM team_aktuell) AS view_summe,
       (SELECT sum(tickets_erstellt) FROM team_bericht) AS matview_summe;
SELECT matviewname, ispopulated FROM pg_matviews WHERE schemaname = 'tickets';

RESET TimeZone;
