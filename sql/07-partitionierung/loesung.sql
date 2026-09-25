-- Musterlösung zu SQL-Übung 7. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Abschnitt 1 entfernt die Übungsobjekte, die Datei lässt sich
-- deshalb wiederholen. Alle Zeitgrenzen tragen ausdrücklich +00, damit die
-- Sitzungszeitzone sie nicht verschiebt.

-- Abschnitt 1: Übungsobjekte entfernen
DROP TABLE IF EXISTS tickets.ticket_partitioned;
DROP TABLE IF EXISTS tickets.ticket_2024q3;

-- Aufgabe 1, erwarteter Fehler mit PRIMARY KEY (id), als eigene Ausführung:
-- 0A000: unique constraint on partitioned table must include all
-- partitioning columns

-- Abschnitt 2 (Aufgabe 1): partitionierte Tabelle mit denselben acht
-- Spalten wie ticket
CREATE TABLE tickets.ticket_partitioned (
    id bigint GENERATED ALWAYS AS IDENTITY,
    agent_id bigint,
    subject text NOT NULL,
    status text NOT NULL,
    priority integer NOT NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL,
    closed_at timestamptz,
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

-- Abschnitt 3 (Aufgabe 2): neun Quartale in UTC und eine DEFAULT-Partition
CREATE TABLE tickets.ticket_2024q3 PARTITION OF tickets.ticket_partitioned
    FOR VALUES FROM ('2024-07-01 00:00+00') TO ('2024-10-01 00:00+00');
CREATE TABLE tickets.ticket_2024q4 PARTITION OF tickets.ticket_partitioned
    FOR VALUES FROM ('2024-10-01 00:00+00') TO ('2025-01-01 00:00+00');
CREATE TABLE tickets.ticket_2025q1 PARTITION OF tickets.ticket_partitioned
    FOR VALUES FROM ('2025-01-01 00:00+00') TO ('2025-04-01 00:00+00');
CREATE TABLE tickets.ticket_2025q2 PARTITION OF tickets.ticket_partitioned
    FOR VALUES FROM ('2025-04-01 00:00+00') TO ('2025-07-01 00:00+00');
CREATE TABLE tickets.ticket_2025q3 PARTITION OF tickets.ticket_partitioned
    FOR VALUES FROM ('2025-07-01 00:00+00') TO ('2025-10-01 00:00+00');
CREATE TABLE tickets.ticket_2025q4 PARTITION OF tickets.ticket_partitioned
    FOR VALUES FROM ('2025-10-01 00:00+00') TO ('2026-01-01 00:00+00');
CREATE TABLE tickets.ticket_2026q1 PARTITION OF tickets.ticket_partitioned
    FOR VALUES FROM ('2026-01-01 00:00+00') TO ('2026-04-01 00:00+00');
CREATE TABLE tickets.ticket_2026q2 PARTITION OF tickets.ticket_partitioned
    FOR VALUES FROM ('2026-04-01 00:00+00') TO ('2026-07-01 00:00+00');
CREATE TABLE tickets.ticket_2026q3 PARTITION OF tickets.ticket_partitioned
    FOR VALUES FROM ('2026-07-01 00:00+00') TO ('2026-10-01 00:00+00');
CREATE TABLE tickets.ticket_default PARTITION OF tickets.ticket_partitioned
    DEFAULT;

-- Abschnitt 4 (Aufgabe 3): Tickets mit ihren IDs kopieren, Identity
-- nachziehen, Statistiken erheben
INSERT INTO tickets.ticket_partitioned
    (id, agent_id, subject, status, priority, metadata, created_at, closed_at)
OVERRIDING SYSTEM VALUE
SELECT id, agent_id, subject, status, priority, metadata, created_at, closed_at
FROM tickets.ticket;
SELECT setval(pg_get_serial_sequence('tickets.ticket_partitioned', 'id'),
              (SELECT max(id) FROM tickets.ticket_partitioned));
ANALYZE tickets.ticket_partitioned;

-- Abschnitt 5 (Aufgabe 4): Bereich auf created_at, eine Partition
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.ticket_partitioned
WHERE created_at >= '2025-01-01 00:00+00' AND created_at < '2025-04-01 00:00+00';

-- Abschnitt 6 (Aufgabe 4): Quartal über date_trunc, alle Partitionen
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.ticket_partitioned
WHERE date_trunc('quarter', created_at AT TIME ZONE 'UTC') = timestamp '2025-01-01';

-- Abschnitt 7 (Aufgabe 4): beide Zählungen liefern 98588
SELECT
    (SELECT count(*) FROM tickets.ticket_partitioned
     WHERE created_at >= '2025-01-01 00:00+00'
       AND created_at < '2025-04-01 00:00+00') AS by_range,
    (SELECT count(*) FROM tickets.ticket_partitioned
     WHERE date_trunc('quarter', created_at AT TIME ZONE 'UTC')
           = timestamp '2025-01-01') AS by_function;

-- Abschnitt 8 (Aufgabe 5): Q3 2024 abtrennen
ALTER TABLE tickets.ticket_partitioned DETACH PARTITION tickets.ticket_2024q3;

-- Abschnitt 9 (Aufgabe 5): abgetrennte Tabelle zählen, 45093 Zeilen
SELECT count(*) AS rows_2024q3 FROM tickets.ticket_2024q3;

-- Abschnitt 10 (Aufgabe 5): passender CHECK, wieder anhängen, CHECK
-- entfernen
ALTER TABLE tickets.ticket_2024q3 ADD CONSTRAINT ticket_2024q3_bounds
    CHECK (created_at >= '2024-07-01 00:00+00' AND created_at < '2024-10-01 00:00+00');
ALTER TABLE tickets.ticket_partitioned ATTACH PARTITION tickets.ticket_2024q3
    FOR VALUES FROM ('2024-07-01 00:00+00') TO ('2024-10-01 00:00+00');
ALTER TABLE tickets.ticket_2024q3 DROP CONSTRAINT ticket_2024q3_bounds;

-- Abschnitt 11: Kontrolle, Zeilen je Partition
SELECT tableoid::regclass AS partition, count(*) AS row_count
FROM tickets.ticket_partitioned
GROUP BY tableoid ORDER BY tableoid::regclass::text;

-- Abschnitt 12: Kontrolle, Zahl der Partitionen
SELECT count(*) AS partitions FROM pg_inherits
WHERE inhparent = 'tickets.ticket_partitioned'::regclass;

-- Abschnitt 13: Kontrolle, Plan für das erste Quartal 2025
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.ticket_partitioned
WHERE created_at >= '2025-01-01 00:00+00' AND created_at < '2025-04-01 00:00+00';
