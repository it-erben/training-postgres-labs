# SQL-Übung 2: Indizes und EXPLAIN

## Ziel

Du liest drei Ausführungspläne mit `EXPLAIN (ANALYZE, BUFFERS)`, legst für
jeden Zugriff einen passenden Index an und vergleichst die Pläne danach
erneut. Zum Schluss prüfst du die Indexgrößen und eine Gegenprobe mit einem
breiteren Zeitfenster.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus.

Drei Zugriffe stehen im Mittelpunkt: offene Tickets von Agents aus dem Team
`Technical`, Tickets mit dem Metadaten-Schlüssel `escalated` und Kommentare
eines Zeitfensters von sieben Tagen.

## Aufgaben

1. Führe für jeden der drei Zugriffe `EXPLAIN (ANALYZE, BUFFERS)` aus und
   notiere Planknoten, geschätzte und tatsächliche Zeilen:

   ```sql
   SET search_path = tickets;
   SET TimeZone = 'UTC';
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT t.id FROM ticket t JOIN agent a ON a.id = t.agent_id
   WHERE a.team = 'Technical' AND t.status = 'open';
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT id FROM ticket WHERE metadata ? 'escalated';
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT count(*) FROM comment
   WHERE created_at >= '2026-01-01' AND created_at < '2026-01-08';
   ```

   `created_at` ist `timestamptz`. Ein Datumsliteral ohne Zonenangabe wertet
   PostgreSQL in der Sitzungszeitzone aus. `SET TimeZone = 'UTC';` macht
   die im Plan gezeigten Literale unabhängig davon, welche Zeitzone die
   Sitzung sonst verwendet.

   Referenzlauf, gekürzt auf die auffälligen Zeilen:

   ```text
    Gather (actual time=24.959..115.752 rows=3784.00 loops=1)
      ->  Hash Join (actual rows=1261.33 loops=3)
            ->  Parallel Seq Scan on ticket t (rows=5089 actual rows=4381.33 loops=3)
                  Filter: (status = 'open'::text)

    Seq Scan on ticket (rows=104066 actual rows=96231.00 loops=1)
      Filter: (metadata ? 'escalated'::text)

    Finalize Aggregate (actual rows=1.00 loops=1)
      ->  Parallel Seq Scan on comment (rows=8345 actual rows=6321.00 loops=3)
            Filter: (created_at >= ... AND created_at < ...)
   ```

   Alle drei Zugriffe lesen die Tabelle vollständig. Die Schätzungen liegen
   nahe an den tatsächlichen Zeilen. Bei diesen Ergebnisanteilen ist ein
   Sequential Scan zulässig.

2. Lege für jeden Zugriff einen Index an:

   ```sql
   CREATE INDEX ticket_open_idx
       ON ticket (agent_id) WHERE status <> 'closed';
   CREATE INDEX ticket_metadata_gin
       ON ticket USING gin (metadata);
   CREATE INDEX comment_created_brin
       ON comment USING brin (created_at);
   ANALYZE ticket;
   ANALYZE comment;
   ```

   `ticket_open_idx` ist ein Teilindex: Er enthält nur Zeilen, deren Status
   nicht `closed` ist, und passt damit zur Filterbedingung `status = 'open'`.
   `ticket_metadata_gin` unterstützt den Existenzoperator `?` auf `jsonb`,
   der prüft, ob ein Schlüssel auf oberster Ebene vorkommt.
   `comment_created_brin` fasst `created_at` blockweise zusammen.

3. Vergleiche die drei Pläne erneut mit denselben `EXPLAIN`-Abfragen wie in
   Aufgabe 1. `ticket_open_idx` und `ticket_metadata_gin` erscheinen
   zuverlässig als `Bitmap Index Scan`. `comment_created_brin` bleibt im
   Referenzlauf ungenutzt: Der Plan zeigt weiterhin einen
   `Parallel Seq Scan on comment`. Notiere, welcher Indexname in welchem
   Plan auftaucht und welcher nicht. Der Hinweis zu `comment_created_brin`
   erklärt, warum das so ist.

4. Vergleiche die Größe der drei neuen Indizes mit der Größe der jeweiligen
   Tabelle:

   ```sql
   SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid))
   FROM pg_stat_user_indexes WHERE schemaname = 'tickets' ORDER BY indexrelname;
   SELECT pg_size_pretty(pg_relation_size('tickets.ticket')) AS ticket_table,
          pg_size_pretty(pg_relation_size('tickets.comment')) AS comment_table;
   ```

5. Gegenprobe: Weite das Zeitfenster der Kommentarabfrage auf ein ganzes
   Jahr aus und sieh dir den Plan an:

   ```sql
   EXPLAIN (COSTS OFF) SELECT count(*) FROM comment
   WHERE created_at >= '2025-01-01' AND created_at < '2026-01-01';
   ```

   Der Plan bleibt ein `Parallel Seq Scan on comment`, wie schon beim
   Sieben-Tage-Fenster. Ein breiteres Fenster ändert hier nichts, weil
   `comment_created_brin` schon beim engen Fenster nichts bringt.

## Ergebnis prüfen

```sql
SET search_path = tickets;
SET TimeZone = 'UTC';
EXPLAIN (COSTS OFF) SELECT t.id FROM ticket t JOIN agent a ON a.id = t.agent_id
WHERE a.team = 'Technical' AND t.status = 'open';
EXPLAIN (COSTS OFF) SELECT id FROM ticket WHERE metadata ? 'escalated';
EXPLAIN (COSTS OFF) SELECT count(*) FROM comment
WHERE created_at >= '2026-01-01' AND created_at < '2026-01-08';
SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid))
FROM pg_stat_user_indexes WHERE schemaname = 'tickets' ORDER BY indexrelname;
RESET TimeZone;
```

Referenzlauf:

```text
 Hash Join
   Hash Cond: (t.agent_id = a.id)
   ->  Bitmap Heap Scan on ticket t
         Recheck Cond: (status <> 'closed'::text)
         Filter: (status = 'open'::text)
         ->  Bitmap Index Scan on ticket_open_idx
   ->  Hash
         ->  Seq Scan on agent a
               Filter: (team = 'Technical'::text)
(9 rows)

 Bitmap Heap Scan on ticket
   Recheck Cond: (metadata ? 'escalated'::text)
   ->  Bitmap Index Scan on ticket_metadata_gin
         Index Cond: (metadata ? 'escalated'::text)
(4 rows)

 Finalize Aggregate
   ->  Gather
         Workers Planned: 2
         ->  Partial Aggregate
               ->  Parallel Seq Scan on comment
                     Filter: ((created_at >= '2026-01-01 00:00:00+00'::timestamp with time zone) AND (created_at < '2026-01-08 00:00:00+00'::timestamp with time zone))
(6 rows)

     indexrelname     | pg_size_pretty
----------------------+----------------
 agent_email_key      | 16 kB
 agent_pkey           | 16 kB
 comment_created_brin | 24 kB
 comment_pkey         | 43 MB
 ticket_metadata_gin  | 6160 kB
 ticket_open_idx      | 296 kB
 ticket_pkey          | 17 MB
(7 rows)
```

Zusätzlich zur Tabellengröße gemessen:

```text
 ticket_table | comment_table
--------------+---------------
 154 MB       | 208 MB
(1 row)
```

`comment_created_brin` erscheint in diesem Ergebnis nicht im dritten Plan,
obwohl der Index angelegt ist. Der Planer hält den `Parallel Seq Scan`
weiterhin für günstiger. Warum, erklärt der Abschnitt "Hinweise".

## Hinweise

`ticket_open_idx` und `ticket_metadata_gin` verkleinern die betroffenen
Zugriffe deutlich. Ein `Bitmap Index Scan` liest nur die passenden
Einträge. Vorher las ein Seq Scan die gesamte Tabelle.

Bei `comment_created_brin` sieht es anders aus. Ein BRIN-Index hält je
Block-Bereich nur Minimum und Maximum der indizierten Spalte fest. Er hilft
nur, wenn benachbarte Zeilen auch ähnliche Werte tragen, also wenn die
physische Reihenfolge der Tabelle mit der Spalte korreliert:

```sql
SELECT attname, correlation FROM pg_stats
WHERE schemaname = 'tickets' AND tablename = 'comment' AND attname = 'created_at';
```

Der Wert liegt im Testlauf nahe 0 (mehrfach zwischen -0.01 und 0.01), also
praktisch unkorreliert. Der Grund liegt in `setup.sql`. Ein Kommentar
erscheint physisch in der Reihenfolge seines Tickets. Sein `created_at`
richtet sich aber nach dem `created_at` des Tickets, und das wählt
`setup.sql` unabhängig von der `id` zufällig aus 730 Tagen. Physisch
benachbarte Kommentare haben deshalb keine benachbarten Zeitstempel.

Mit `EXPLAIN (ANALYZE, BUFFERS)` und erzwungenem `enable_seqscan = off`
lässt sich das direkt zeigen: Der `Bitmap Index Scan` auf
`comment_created_brin` liefert zwar nur wenige Indexseiten, aber die
`Bitmap Heap Scan`-Zeile zeigt danach `Heap Blocks: lossy` in Höhe der
gesamten Tabelle, `relpages` von `comment` eingeschlossen. Der Index kann
also keinen einzigen Block ausschließen. Er liest de facto dieselbe Menge
Daten wie ein `Seq Scan`, nur mit zusätzlichem Rechecken. In seltenen
Läufen liefert die zufällige `ANALYZE`-Stichprobe eine minimal andere
Schätzung, und der Planer wählt genau diesen `Bitmap Heap Scan` über
`comment_created_brin`. Beide Pläne lesen dieselbe Datenmenge. Für das
Ergebnis der Übung ist egal, welcher erscheint.

`comment_created_brin` bleibt mit 24 kB trotzdem der mit Abstand kleinste
der drei neuen Indizes, `comment_pkey` belegt 43 MB. Ein BRIN-Index lohnt
sich für Spalten, die tatsächlich mit der Einfügereihenfolge korrelieren,
etwa eine echte Ereigniszeit in einer append-only Tabelle. Das zufällig
verteilte Datum hier gehört nicht dazu.

`loesung.sql` verwendet `CREATE INDEX IF NOT EXISTS` und lässt sich deshalb
mehrfach ausführen, ohne vorhandene Indizes erneut anzulegen.
