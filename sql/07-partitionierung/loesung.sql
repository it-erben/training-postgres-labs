-- Musterlösung zu SQL-Übung 7. Läuft vollständig im Query Tool und
-- lässt sich wiederholen: Die ersten Anweisungen entfernen die Übungsobjekte.
DROP TABLE IF EXISTS tickets.ticket_partitioned;
DROP TABLE IF EXISTS tickets.ticket_2024q3;

-- Aufgabe 1: Partitionierte Tabelle mit denselben acht Spalten wie ticket
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

-- Aufgabe 2: Neun Quartale in UTC und eine DEFAULT-Partition
-- Die Grenzen tragen ausdrücklich +00, damit die Sitzungszeitzone sie nicht verschiebt.
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

-- Aufgabe 3: Tickets mit ihren IDs kopieren, Identity nachziehen
INSERT INTO tickets.ticket_partitioned
    (id, agent_id, subject, status, priority, metadata, created_at, closed_at)
OVERRIDING SYSTEM VALUE
SELECT id, agent_id, subject, status, priority, metadata, created_at, closed_at
FROM tickets.ticket;
SELECT setval(pg_get_serial_sequence('tickets.ticket_partitioned', 'id'),
              (SELECT max(id) FROM tickets.ticket_partitioned));
ANALYZE tickets.ticket_partitioned;

-- Aufgabe 4: Quartal über einen Bereich auf created_at und über date_trunc
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.ticket_partitioned
WHERE created_at >= '2025-01-01 00:00+00' AND created_at < '2025-04-01 00:00+00';
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.ticket_partitioned
WHERE date_trunc('quarter', created_at AT TIME ZONE 'UTC') = timestamp '2025-01-01';

-- Aufgabe 5: Q3 2024 abtrennen, zählen und mit passendem CHECK wieder anhängen
ALTER TABLE tickets.ticket_partitioned DETACH PARTITION tickets.ticket_2024q3;
SELECT count(*) AS rows_2024q3 FROM tickets.ticket_2024q3;
ALTER TABLE tickets.ticket_2024q3 ADD CONSTRAINT ticket_2024q3_bounds
    CHECK (created_at >= '2024-07-01 00:00+00' AND created_at < '2024-10-01 00:00+00');
ALTER TABLE tickets.ticket_partitioned ATTACH PARTITION tickets.ticket_2024q3
    FOR VALUES FROM ('2024-07-01 00:00+00') TO ('2024-10-01 00:00+00');
ALTER TABLE tickets.ticket_2024q3 DROP CONSTRAINT ticket_2024q3_bounds;

-- Kontrolle: Zeilen je Partition
SELECT tableoid::regclass AS partition, count(*) AS row_count
FROM tickets.ticket_partitioned
GROUP BY tableoid ORDER BY tableoid::regclass::text;
