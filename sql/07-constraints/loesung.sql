-- Musterlösung zu SQL-Übung 7. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Abschnitt 1 entfernt alle Übungsobjekte, die Datei lässt sich
-- deshalb wiederholen. ticket_metadata_gin (Übungen 4 und 6) bleibt
-- unberührt.

-- Abschnitt 1: Übungsobjekte entfernen
DROP TABLE IF EXISTS tickets.ticket_ref;
DROP TABLE IF EXISTS tickets.on_call;
ALTER TABLE tickets.ticket DROP CONSTRAINT IF EXISTS ticket_priority_check;
DROP INDEX IF EXISTS tickets.comment_parent_idx;

-- Abschnitt 2 (Aufgabe 1): CHECK zuerst NOT VALID anlegen, als eigene,
-- sofort bestätigte Ausführung. Liefe ADD CONSTRAINT in derselben
-- Transaktion wie VALIDATE CONSTRAINT, bliebe seine ACCESS EXCLUSIVE-Sperre
-- bis zum COMMIT bestehen und würde das UPDATE in B blockieren.
ALTER TABLE tickets.ticket
    ADD CONSTRAINT ticket_priority_check
    CHECK (priority BETWEEN 1 AND 4) NOT VALID;

-- Aufgabe 1 mit zwei Verbindungen. Ergebnis siehe AUFGABE.md:
-- ShareUpdateExclusiveLock, granted = t, und UPDATE 1 in B läuft trotz
-- offener Transaktion in A durch.
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

-- Abschnitt 3 (Aufgabe 1): Der ausführbare Teil validiert in einer
-- Verbindung.
ALTER TABLE tickets.ticket VALIDATE CONSTRAINT ticket_priority_check;

-- Abschnitt 4 (Aufgabe 2): Fremdschlüssel ohne unterstützenden Index.
-- Ergebnis: comment_parent_id_fkey, comment_ticket_id_fkey,
-- ticket_agent_id_fkey. Ein Teilindex wie ticket_open_idx aus Übung 4
-- zählt nicht, weil er nur einen Teil der Zeilen enthält.
SELECT c.conrelid::regclass AS table_name, c.conname
FROM pg_constraint c
WHERE c.contype = 'f'
  AND c.connamespace = 'tickets'::regnamespace
  AND NOT EXISTS (
      SELECT 1 FROM pg_index i
      WHERE i.indrelid = c.conrelid
        AND i.indkey[0] = c.conkey[1]
        AND i.indpred IS NULL
  )
ORDER BY c.conname;

-- Abschnitt 5 (Aufgabe 2): Index für comment.parent_id
CREATE INDEX comment_parent_idx ON tickets.comment (parent_id);

-- Abschnitt 6 (Aufgabe 3): Bereitschaftsplan ohne überlappende Zeiträume
-- je Agent
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE tickets.on_call (
    agent_id bigint REFERENCES tickets.agent(id),
    time_range tstzrange NOT NULL,
    EXCLUDE USING gist (agent_id WITH =, time_range WITH &&)
);

-- Abschnitt 7 (Aufgabe 4): zwei angrenzende Zeiträume, erlaubt
INSERT INTO tickets.on_call (agent_id, time_range) VALUES
    (1, tstzrange('2026-09-01 00:00+00', '2026-09-08 00:00+00', '[)')),
    (1, tstzrange('2026-09-08 00:00+00', '2026-09-15 00:00+00', '[)'));

-- Aufgabe 4, erwarteter Fehler, im Query Tool als eigene Ausführung:
-- INSERT INTO tickets.on_call (agent_id, time_range)
-- VALUES (1, tstzrange('2026-09-05 00:00+00', '2026-09-10 00:00+00', '[)'));
-- -- ERROR: 23P01: conflicting key value violates exclusion constraint
-- -- "on_call_agent_id_time_range_excl"

-- Abschnitt 8 (Aufgabe 5): aufschiebbarer Fremdschlüssel
CREATE TABLE tickets.ticket_ref (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ticket_id bigint NOT NULL
        REFERENCES tickets.ticket(id) DEFERRABLE INITIALLY DEFERRED
);

-- Aufgabe 5, erwarteter Fehler beim COMMIT, im Query Tool als zwei
-- Ausführungen:
-- BEGIN;
-- INSERT INTO tickets.ticket_ref (ticket_id) VALUES (9999999);
-- -- INSERT 0 1, ohne Fehler: Ticket 9999999 existiert nicht.
-- COMMIT;
-- -- ERROR: 23503: insert or update on table "ticket_ref" violates
-- -- foreign key constraint "ticket_ref_ticket_id_fkey"

-- Abschnitt 9: Kontrolle der beiden Regeln
SELECT conname, contype, convalidated FROM pg_constraint
WHERE conrelid IN ('tickets.ticket'::regclass, 'tickets.on_call'::regclass)
  AND contype IN ('c', 'x') ORDER BY conname;

-- Abschnitt 10: Kontrolle der Bereitschaftszeiten
SELECT agent_id, time_range FROM tickets.on_call
ORDER BY agent_id, lower(time_range);
