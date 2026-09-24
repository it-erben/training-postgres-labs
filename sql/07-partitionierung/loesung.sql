-- Musterlösung zu SQL-Übung 7. Läuft vollständig im Query Tool und
-- lässt sich wiederholen: Die ersten Anweisungen entfernen die Übungsobjekte.
DROP TABLE IF EXISTS tickets.ticket_partitioniert;
DROP TABLE IF EXISTS tickets.ticket_2024q3;

-- Aufgabe 1: Partitionierte Tabelle mit denselben acht Spalten wie ticket
CREATE TABLE tickets.ticket_partitioniert (
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
CREATE TABLE tickets.ticket_2024q3 PARTITION OF tickets.ticket_partitioniert
    FOR VALUES FROM ('2024-07-01 00:00+00') TO ('2024-10-01 00:00+00');
CREATE TABLE tickets.ticket_2024q4 PARTITION OF tickets.ticket_partitioniert
    FOR VALUES FROM ('2024-10-01 00:00+00') TO ('2025-01-01 00:00+00');
CREATE TABLE tickets.ticket_2025q1 PARTITION OF tickets.ticket_partitioniert
    FOR VALUES FROM ('2025-01-01 00:00+00') TO ('2025-04-01 00:00+00');
CREATE TABLE tickets.ticket_2025q2 PARTITION OF tickets.ticket_partitioniert
    FOR VALUES FROM ('2025-04-01 00:00+00') TO ('2025-07-01 00:00+00');
CREATE TABLE tickets.ticket_2025q3 PARTITION OF tickets.ticket_partitioniert
    FOR VALUES FROM ('2025-07-01 00:00+00') TO ('2025-10-01 00:00+00');
CREATE TABLE tickets.ticket_2025q4 PARTITION OF tickets.ticket_partitioniert
    FOR VALUES FROM ('2025-10-01 00:00+00') TO ('2026-01-01 00:00+00');
CREATE TABLE tickets.ticket_2026q1 PARTITION OF tickets.ticket_partitioniert
    FOR VALUES FROM ('2026-01-01 00:00+00') TO ('2026-04-01 00:00+00');
CREATE TABLE tickets.ticket_2026q2 PARTITION OF tickets.ticket_partitioniert
    FOR VALUES FROM ('2026-04-01 00:00+00') TO ('2026-07-01 00:00+00');
CREATE TABLE tickets.ticket_2026q3 PARTITION OF tickets.ticket_partitioniert
    FOR VALUES FROM ('2026-07-01 00:00+00') TO ('2026-10-01 00:00+00');
CREATE TABLE tickets.ticket_default PARTITION OF tickets.ticket_partitioniert
    DEFAULT;

-- Aufgabe 3: Tickets mit ihren IDs kopieren, Identity nachziehen
INSERT INTO tickets.ticket_partitioniert
    (id, agent_id, subject, status, priority, metadata, created_at, closed_at)
OVERRIDING SYSTEM VALUE
SELECT id, agent_id, subject, status, priority, metadata, created_at, closed_at
FROM tickets.ticket;
SELECT setval(pg_get_serial_sequence('tickets.ticket_partitioniert', 'id'),
              (SELECT max(id) FROM tickets.ticket_partitioniert));
ANALYZE tickets.ticket_partitioniert;

-- Aufgabe 4: Quartal über einen Bereich auf created_at und über date_trunc
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.ticket_partitioniert
WHERE created_at >= '2025-01-01 00:00+00' AND created_at < '2025-04-01 00:00+00';
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.ticket_partitioniert
WHERE date_trunc('quarter', created_at AT TIME ZONE 'UTC') = timestamp '2025-01-01';

-- Aufgabe 5: Q3 2024 abtrennen, zählen und mit passendem CHECK wieder anhängen
ALTER TABLE tickets.ticket_partitioniert DETACH PARTITION tickets.ticket_2024q3;
SELECT count(*) AS zeilen_2024q3 FROM tickets.ticket_2024q3;
ALTER TABLE tickets.ticket_2024q3 ADD CONSTRAINT ticket_2024q3_grenzen
    CHECK (created_at >= '2024-07-01 00:00+00' AND created_at < '2024-10-01 00:00+00');
ALTER TABLE tickets.ticket_partitioniert ATTACH PARTITION tickets.ticket_2024q3
    FOR VALUES FROM ('2024-07-01 00:00+00') TO ('2024-10-01 00:00+00');
ALTER TABLE tickets.ticket_2024q3 DROP CONSTRAINT ticket_2024q3_grenzen;

-- Kontrolle: Zeilen je Partition
SELECT tableoid::regclass AS partition, count(*) AS zeilen
FROM tickets.ticket_partitioniert
GROUP BY tableoid ORDER BY tableoid::regclass::text;
