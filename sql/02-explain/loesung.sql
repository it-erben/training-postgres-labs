-- Musterlösung zu SQL-Übung 2. Läuft vollständig im Query Tool und lässt
-- sich wiederholen: CREATE INDEX IF NOT EXISTS überspringt vorhandene Indizes.
SET search_path = tickets;

-- Aufgabe 1: Ausgangspläne der drei Zugriffe (Befunde siehe AUFGABE.md)
EXPLAIN (ANALYZE, BUFFERS)
SELECT t.id FROM ticket t JOIN agent a ON a.id = t.agent_id
WHERE a.team = 'Technik' AND t.status = 'open';
EXPLAIN (ANALYZE, BUFFERS)
SELECT id FROM ticket WHERE metadata ? 'escalated';
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*) FROM comment
WHERE created_at >= '2026-01-01' AND created_at < '2026-01-08';

-- Aufgabe 2: je Zugriff ein Index
CREATE INDEX IF NOT EXISTS ticket_offen_idx
    ON ticket (agent_id) WHERE status <> 'closed';
CREATE INDEX IF NOT EXISTS ticket_metadata_gin
    ON ticket USING gin (metadata);
CREATE INDEX IF NOT EXISTS comment_created_brin
    ON comment USING brin (created_at);
ANALYZE ticket;
ANALYZE comment;

-- Aufgabe 3: Pläne erneut vergleichen. ticket_offen_idx und
-- ticket_metadata_gin erscheinen zuverlässig. comment_created_brin bleibt
-- ungenutzt, weil created_at nicht mit der physischen Reihenfolge von
-- comment korreliert; siehe AUFGABE.md und Hinweise.
EXPLAIN (COSTS OFF) SELECT t.id FROM ticket t JOIN agent a ON a.id = t.agent_id
WHERE a.team = 'Technik' AND t.status = 'open';
EXPLAIN (COSTS OFF) SELECT id FROM ticket WHERE metadata ? 'escalated';
EXPLAIN (COSTS OFF) SELECT count(*) FROM comment
WHERE created_at >= '2026-01-01' AND created_at < '2026-01-08';

-- Aufgabe 4: Indexgrößen gegenüber der Tabelle
SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid))
FROM pg_stat_user_indexes WHERE schemaname = 'tickets' ORDER BY indexrelname;
SELECT pg_size_pretty(pg_relation_size('tickets.ticket')) AS ticket_tabelle,
       pg_size_pretty(pg_relation_size('tickets.comment')) AS comment_tabelle;

-- Aufgabe 5: Gegenprobe mit einem Zeitfenster von einem Jahr
EXPLAIN (COSTS OFF) SELECT count(*) FROM comment
WHERE created_at >= '2025-01-01' AND created_at < '2026-01-01';
