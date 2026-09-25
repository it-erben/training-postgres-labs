-- Musterlösung zu SQL-Übung 1. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Am Stück scheitert die Datei an VACUUM (25001), außerdem lesen
-- die Messungen dann Zählerstände, die noch nicht veröffentlicht sind.
-- Ein zweiter Lauf ist möglich: Die gemerkten Startwerte werden bei jedem
-- Lauf neu gesetzt, und der letzte Abschnitt stellt den Betreff von
-- Ticket 1 wieder her.

-- Abschnitt 1 (Aufgabe 1): eigene Sitzung in pg_stat_activity. Die zweite
-- Verbindung exercise-b erscheint nur, wenn ein zweites Query Tool sie setzt.
SET application_name = 'exercise-a';
SELECT application_name, pid = pg_backend_pid() AS own_session,
       backend_type, state
FROM pg_stat_activity
WHERE datname = current_database()
ORDER BY application_name;

-- Abschnitt 2 (Aufgabe 2, Schritt 1): alte und neue Zeilenversion. Nach
-- frischem setup.sql wandert die Version von Seite 0 auf die letzte Seite,
-- hot_updates ist 0.
UPDATE tickets.ticket SET priority = priority WHERE id = 1
RETURNING old.ctid AS old_ctid, new.ctid AS new_ctid,
          old.xmin AS old_xmin, old.xmax AS old_xmax,
          new.xmin AS new_xmin,
          pg_stat_get_xact_tuples_hot_updated('tickets.ticket'::regclass)
              AS hot_updates;

-- Abschnitt 3 (Aufgabe 2, Schritt 2): dasselbe UPDATE noch einmal. Die
-- letzte Seite hat Platz, die neue Version bleibt dort, hot_updates ist 1.
UPDATE tickets.ticket SET priority = priority WHERE id = 1
RETURNING old.ctid AS old_ctid, new.ctid AS new_ctid,
          old.xmin AS old_xmin, old.xmax AS old_xmax,
          new.xmin AS new_xmin,
          pg_stat_get_xact_tuples_hot_updated('tickets.ticket'::regclass)
              AS hot_updates;

-- Abschnitt 4 (Aufgabe 3): Ein Skript hat nur eine Verbindung. Ausführbar
-- ist deshalb nur die Änderung von A, hier ohne offene Transaktion. Der
-- Ablauf mit zwei Verbindungen steht darunter als Kommentar.
UPDATE tickets.ticket SET subject = subject || ' (A)' WHERE id = 1;

-- Verbindung A:
-- BEGIN;
-- UPDATE tickets.ticket SET subject = subject || ' (A)' WHERE id = 1;
--
-- Verbindung B, während A offen ist. Liefert sofort die alte Version:
-- alter Betreff, alte ctid, xmax = Transaktionsnummer von A.
-- SELECT ctid, xmin, xmax, subject FROM tickets.ticket WHERE id = 1;
--
-- Verbindung A. Sieht die eigene neue Version mit xmax = 0.
-- SELECT ctid, xmin, xmax, subject FROM tickets.ticket WHERE id = 1;
-- COMMIT;
--
-- Verbindung B, neue Abfrage nach dem COMMIT. Liefert die neue Version.
-- SELECT ctid, xmin, xmax, subject FROM tickets.ticket WHERE id = 1;

-- Abschnitt 5 (Aufgabe 4, Schritt 1): WAL-Position merken
SELECT set_config('exercise.wal_start', pg_current_wal_lsn()::text, false);

-- Abschnitt 6 (Aufgabe 4, Schritt 2): Auto commit schließt das UPDATE mit
-- seinem COMMIT ab. Spätestens dann ist sein WAL geschrieben.
UPDATE tickets.ticket SET priority = priority WHERE id = 1;

-- Abschnitt 7 (Aufgabe 4, Schritt 3): WAL seit der gemerkten Position
SELECT pg_wal_lsn_diff(pg_current_wal_lsn(),
                       current_setting('exercise.wal_start')::pg_lsn)
           AS wal_bytes,
       (SELECT checkpoint_time FROM pg_control_checkpoint())
           AS last_checkpoint;

-- Abschnitt 8 (Aufgabe 5, Schritt 1): Autovacuum für ticket aus, damit kein
-- automatischer Lauf die Zählerstände verändert
ALTER TABLE tickets.ticket SET (autovacuum_enabled = false);

-- Abschnitt 9 (Aufgabe 5, Schritt 2): Tabellengröße merken
SELECT set_config('exercise.size_start',
                  pg_relation_size('tickets.ticket')::text, false);

-- Abschnitt 10 (Aufgabe 5, Schritt 3). 20000 Zeilen betreffen mehr als
-- 2 % der Seiten. Darunter überspringt VACUUM die Indexbereinigung, und der
-- frei gewordene Platz steht dem zweiten UPDATE nicht zur Verfügung.
-- pg_stat_force_next_flush() veröffentlicht die Zähler am Ende der
-- Ausführung.
UPDATE tickets.ticket SET priority = priority WHERE id <= 20000;
SELECT pg_stat_force_next_flush();

-- Abschnitt 11 (Aufgabe 5, Schritt 4): tote Tupel und neue Seiten. Die
-- Zähler des UPDATE sind erst nach dem Ende seiner Transaktion sichtbar,
-- deshalb eine eigene Ausführung.
SELECT n_dead_tup,
       (pg_relation_size('tickets.ticket')
        - current_setting('exercise.size_start')::bigint) / 8192
           AS new_pages
FROM pg_stat_user_tables
WHERE relid = 'tickets.ticket'::regclass;

-- Abschnitt 12 (Aufgabe 5, Schritt 5): VACUUM allein, außerhalb eines
-- Transaktionsblocks
VACUUM tickets.ticket;

-- Abschnitt 13 (Aufgabe 5, Schritt 6): n_dead_tup ist 0, new_pages bleibt
SELECT n_dead_tup,
       (pg_relation_size('tickets.ticket')
        - current_setting('exercise.size_start')::bigint) / 8192
           AS new_pages
FROM pg_stat_user_tables
WHERE relid = 'tickets.ticket'::regclass;

-- Abschnitt 14 (Aufgabe 5, Schritt 7): dieselben Zeilen erneut
UPDATE tickets.ticket SET priority = priority WHERE id <= 20000;
SELECT pg_stat_force_next_flush();

-- Abschnitt 15 (Aufgabe 5, Schritt 8): new_pages wächst nicht, die neuen
-- Versionen belegen den von VACUUM freigegebenen Platz
SELECT n_dead_tup,
       (pg_relation_size('tickets.ticket')
        - current_setting('exercise.size_start')::bigint) / 8192
           AS new_pages
FROM pg_stat_user_tables
WHERE relid = 'tickets.ticket'::regclass;

-- Abschnitt 16 (Aufgabe 5, Schritt 9)
VACUUM tickets.ticket;

-- Abschnitt 17 (Aufgabe 5, Schritt 10): Autovacuum wieder ein
ALTER TABLE tickets.ticket RESET (autovacuum_enabled);

-- Abschnitt 18: Prüfabfrage, erwartet t | t | t
SELECT
    (SELECT subject LIKE '% (A)'
     FROM tickets.ticket WHERE id = 1) AS subject_from_a,
    (SELECT n_dead_tup = 0
     FROM pg_stat_user_tables
     WHERE relid = 'tickets.ticket'::regclass) AS no_dead_tuples,
    (SELECT NOT coalesce('autovacuum_enabled=false' = ANY (reloptions), false)
     FROM pg_class
     WHERE oid = 'tickets.ticket'::regclass) AS autovacuum_on;

-- Abschnitt 19: Betreff von Ticket 1 wiederherstellen
UPDATE tickets.ticket
SET subject = 'Ticket #1: Fehlermeldung beim Checkout'
WHERE id = 1;
