-- Musterlösung zu SQL-Übung 4. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Am Stück zeigt Data Output nur das Ergebnis der letzten
-- Anweisung. CREATE INDEX IF NOT EXISTS überspringt vorhandene Indizes,
-- deshalb lässt sich die Datei wiederholen. Die Zeitgrenzen tragen die Zone
-- +00 und gelten unabhängig von der Sitzungszeitzone.

-- Abschnitt 1 (Aufgabe 1.1): offene Tickets aus Technical
EXPLAIN (ANALYZE, BUFFERS)
SELECT t.id
FROM tickets.ticket t
JOIN tickets.agent a ON a.id = t.agent_id
WHERE a.team = 'Technical' AND t.status = 'open';

-- Abschnitt 2 (Aufgabe 1.2): Tickets mit dem Schlüssel escalated
EXPLAIN (ANALYZE, BUFFERS)
SELECT id FROM tickets.ticket WHERE metadata ? 'escalated';

-- Abschnitt 3 (Aufgabe 1.3): Kommentare aus sieben Tagen
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*) FROM tickets.comment
WHERE created_at >= '2026-01-01 00:00+00'
  AND created_at < '2026-01-08 00:00+00';

-- Abschnitt 4 (Aufgabe 2): je Zugriff ein Index, danach Statistiken
CREATE INDEX IF NOT EXISTS ticket_open_idx
    ON tickets.ticket (agent_id) WHERE status <> 'closed';
CREATE INDEX IF NOT EXISTS ticket_metadata_gin
    ON tickets.ticket USING gin (metadata);
CREATE INDEX IF NOT EXISTS comment_created_brin
    ON tickets.comment USING brin (created_at);
ANALYZE tickets.ticket;
ANALYZE tickets.comment;

-- Abschnitt 5 (Aufgabe 3.1): Bitmap Index Scan on ticket_open_idx. Die
-- offenen Tickets liegen verstreut, der Bitmap Heap Scan liest trotzdem den
-- größten Teil der Tabelle.
EXPLAIN (ANALYZE, BUFFERS)
SELECT t.id
FROM tickets.ticket t
JOIN tickets.agent a ON a.id = t.agent_id
WHERE a.team = 'Technical' AND t.status = 'open';

-- Abschnitt 6 (Aufgabe 3.2): Bitmap Index Scan on ticket_metadata_gin
EXPLAIN (ANALYZE, BUFFERS)
SELECT id FROM tickets.ticket WHERE metadata ? 'escalated';

-- Abschnitt 7 (Aufgabe 3.3): weiterhin Parallel Seq Scan on comment.
-- created_at korreliert nicht mit der physischen Reihenfolge, der
-- BRIN-Index schließt keinen Block aus.
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*) FROM tickets.comment
WHERE created_at >= '2026-01-01 00:00+00'
  AND created_at < '2026-01-08 00:00+00';

-- Abschnitt 8 (Aufgabe 4.1): Indexgrößen
SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid))
FROM pg_stat_user_indexes
WHERE schemaname = 'tickets' AND relname IN ('agent', 'ticket', 'comment')
ORDER BY indexrelname;

-- Abschnitt 9 (Aufgabe 4.2): Tabellengrößen
SELECT pg_size_pretty(pg_relation_size('tickets.ticket')) AS ticket_table,
       pg_size_pretty(pg_relation_size('tickets.comment')) AS comment_table;

-- Abschnitt 10 (Aufgabe 5): Gegenprobe mit einem ganzen Jahr
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.comment
WHERE created_at >= '2025-01-01 00:00+00'
  AND created_at < '2026-01-01 00:00+00';

-- Abschnitt 11: Korrelation von created_at mit der physischen Reihenfolge
SELECT attname, correlation FROM pg_stats
WHERE schemaname = 'tickets' AND tablename = 'comment' AND attname = 'created_at';
