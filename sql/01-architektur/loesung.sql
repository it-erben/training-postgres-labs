-- Musterlösung zu SQL-Übung 1. Läuft vollständig im Query Tool und
-- lässt sich wiederholen: Die erste Anweisung entfernt tickets.messung.
DROP TABLE IF EXISTS tickets.messung;
CREATE TABLE tickets.messung (
    schritt text PRIMARY KEY,
    wert text
);

-- Aufgabe 1: Eigene Sitzung und eine zweite Verbindung in pg_stat_activity
-- finden. Eine zweite Verbindung mit eigenem application_name gehört dazu;
-- dieses Skript zeigt nur die eigene Zeile.
SELECT application_name, backend_type, state
FROM pg_stat_activity
WHERE datname = current_database()
ORDER BY application_name;

-- Aufgabe 2: xmin und ctid von Ticket 1 vor und nach einem UPDATE.
-- _vorher merkt sich beide Werte, damit derselbe Lauf vorher und nachher
-- vergleichen kann.
CREATE TEMP TABLE _vorher AS
SELECT xmin AS xmin_vorher, ctid AS ctid_vorher FROM tickets.ticket WHERE id = 1;

UPDATE tickets.ticket SET priority = priority WHERE id = 1;

INSERT INTO tickets.messung (schritt, wert)
SELECT 'ctid_geaendert', (t.ctid <> v.ctid_vorher)::text
FROM tickets.ticket t, _vorher v
WHERE t.id = 1;

DROP TABLE _vorher;

-- Aufgabe 3: Zwei Verbindungen, eine offene Transaktion. Eine einzelne
-- Skriptausführung hat keine zweite Verbindung; die Schritte stehen deshalb
-- als Kommentar.
--
-- Verbindung A:
-- BEGIN;
-- UPDATE tickets.ticket SET subject = subject || ' (A)' WHERE id = 1;
-- -- Transaktion bleibt offen.
--
-- Verbindung B (während A offen ist):
-- SELECT id, subject FROM tickets.ticket WHERE id = 1;
-- -- Liefert den alten Wert, der Snapshot von B sieht die offene Änderung von A nicht.
--
-- Verbindung A:
-- COMMIT;
--
-- Verbindung B (nach dem COMMIT, neue Abfrage):
-- SELECT id, subject FROM tickets.ticket WHERE id = 1;
-- -- Liefert jetzt den neuen Wert.

-- Aufgabe 4: 10000 Tickets aktualisieren, tote Tupel vor und nach VACUUM.
-- autovacuum_enabled aus, damit kein automatischer Lauf dazwischenfunkt;
-- am Ende wieder zurücksetzen.
ALTER TABLE tickets.ticket SET (autovacuum_enabled = false);

UPDATE tickets.ticket SET priority = priority WHERE id <= 10000;
SELECT pg_stat_force_next_flush();

INSERT INTO tickets.messung (schritt, wert)
SELECT 'tote_tupel_nach_update', n_dead_tup::text
FROM pg_stat_user_tables WHERE schemaname = 'tickets' AND relname = 'ticket';

VACUUM tickets.ticket;
SELECT pg_stat_force_next_flush();

INSERT INTO tickets.messung (schritt, wert)
SELECT 'tote_tupel_nach_vacuum', n_dead_tup::text
FROM pg_stat_user_tables WHERE schemaname = 'tickets' AND relname = 'ticket';

ALTER TABLE tickets.ticket RESET (autovacuum_enabled);

-- Aufgabe 5: WAL-Menge eines UPDATE mit pg_current_wal_lsn() und
-- pg_wal_lsn_diff messen. _wal_start merkt sich die Position vor dem UPDATE.
CREATE TEMP TABLE _wal_start AS SELECT pg_current_wal_lsn() AS lsn;

UPDATE tickets.ticket SET priority = priority WHERE id = 1;

INSERT INTO tickets.messung (schritt, wert)
SELECT 'wal_bytes', pg_wal_lsn_diff(pg_current_wal_lsn(), lsn)::text
FROM _wal_start;

DROP TABLE _wal_start;

-- Kontrolle
SELECT schritt, wert FROM tickets.messung ORDER BY schritt;
