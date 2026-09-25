-- Musterlösung zu SQL-Übung 6. Läuft vollständig im Query Tool und lässt
-- sich wiederholen: Der Rücksetzblock entfernt zu Beginn alle
-- Übungsobjekte. ticket_metadata_gin (Übungen 2 und 5) bleibt unberührt.
SET search_path = tickets;

DROP TABLE IF EXISTS tickets.ticket_ref;
DROP TABLE IF EXISTS tickets.on_call;
ALTER TABLE ticket DROP CONSTRAINT IF EXISTS ticket_priority_check;
DROP INDEX IF EXISTS comment_parent_idx;

-- Aufgabe 1: CHECK zuerst NOT VALID anlegen (eigene, sofort bestätigte
-- Anweisung), danach VALIDATE CONSTRAINT in einer offenen Transaktion
-- beobachten. Mit zwei Verbindungen wird sichtbar, dass VALIDATE
-- CONSTRAINT nur SHARE UPDATE EXCLUSIVE hält. Ergebnis siehe AUFGABE.md:
-- ShareUpdateExclusiveLock, granted = t, und UPDATE 1 in B läuft trotz
-- offener Transaktion in A durch. Liefe ADD CONSTRAINT in derselben
-- Transaktion wie VALIDATE CONSTRAINT, bliebe seine ACCESS EXCLUSIVE-Sperre
-- bis zum COMMIT bestehen und würde das UPDATE in B blockieren.
ALTER TABLE ticket
    ADD CONSTRAINT ticket_priority_check CHECK (priority BETWEEN 1 AND 4) NOT VALID;
--
-- Verbindung A:
-- SET application_name = 'exercise_a';
-- BEGIN;
-- ALTER TABLE tickets.ticket VALIDATE CONSTRAINT ticket_priority_check;
--
-- Verbindung B (während A offen ist):
-- SELECT a.application_name, l.mode, l.granted
-- FROM pg_locks l
-- JOIN pg_stat_activity a ON a.pid = l.pid
-- WHERE a.application_name = 'exercise_a' AND l.relation = 'tickets.ticket'::regclass
-- ORDER BY l.mode;
-- UPDATE tickets.ticket SET priority = priority WHERE id = 1;
-- -- UPDATE 1, ohne zu warten: SHARE UPDATE EXCLUSIVE blockiert kein DML.
--
-- Verbindung A:
-- COMMIT;
--
-- Der ausführbare Teil bildet denselben Effekt in einer Verbindung nach.
ALTER TABLE ticket VALIDATE CONSTRAINT ticket_priority_check;

-- Aufgabe 2: Fremdschlüssel ohne unterstützenden Index finden (Ergebnis
-- siehe AUFGABE.md: comment_parent_id_fkey, comment_ticket_id_fkey,
-- ticket_agent_id_fkey), Index für comment.parent_id anlegen
CREATE INDEX comment_parent_idx ON comment (parent_id);

-- Aufgabe 3: Bereitschaftsplan ohne überlappende Zeiträume je Agent
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE on_call (
    agent_id bigint REFERENCES agent(id),
    time_range tstzrange NOT NULL,
    EXCLUDE USING gist (agent_id WITH =, time_range WITH &&)
);

-- Aufgabe 4: zwei angrenzende Zeiträume (erlaubt), ein überlappender (23P01)
INSERT INTO on_call (agent_id, time_range) VALUES
    (1, tstzrange('2026-09-01 00:00+00', '2026-09-08 00:00+00', '[)')),
    (1, tstzrange('2026-09-08 00:00+00', '2026-09-15 00:00+00', '[)'));
-- INSERT INTO on_call (agent_id, time_range)
-- VALUES (1, tstzrange('2026-09-05 00:00+00', '2026-09-10 00:00+00', '[)'));
-- -- ERROR: 23P01: conflicting key value violates exclusion constraint
-- -- "on_call_agent_id_time_range_excl"

-- Aufgabe 5: aufschiebbarer Fremdschlüssel, Prüfung erst beim COMMIT
CREATE TABLE ticket_ref (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ticket_id bigint NOT NULL REFERENCES ticket(id) DEFERRABLE INITIALLY DEFERRED
);
-- BEGIN;
-- INSERT INTO ticket_ref (ticket_id) VALUES (9999999);
-- -- INSERT 0 1, ohne Fehler: Ticket 9999999 existiert nicht.
-- COMMIT;
-- -- ERROR: 23503: insert or update on table "ticket_ref" violates
-- -- foreign key constraint "ticket_ref_ticket_id_fkey"

-- Kontrolle (Ergebnis siehe AUFGABE.md)
SELECT conname, contype, convalidated FROM pg_constraint
WHERE conrelid IN ('ticket'::regclass, 'on_call'::regclass)
  AND contype IN ('c', 'x') ORDER BY conname;
SELECT agent_id, time_range FROM on_call ORDER BY agent_id, lower(time_range);
