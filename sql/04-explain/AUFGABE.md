# SQL-Übung 4: Indizes und EXPLAIN

## Ziel

Du liest drei Ausführungspläne mit `EXPLAIN (ANALYZE, BUFFERS)`, legst für
jeden Zugriff einen passenden Index an und vergleichst die Pläne danach
erneut. Zum Schluss prüfst du die Indexgrößen und eine Gegenprobe mit einem
breiteren Zeitfenster. Vor jedem Plan legst du dich auf eine Antwort fest,
die [Auflösung](#auflösung) am Ende zeigt die Referenzpläne.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus. Jeder Codeblock
ist eine Ausführung, wie in Übung 0 beschrieben.

Drei Zugriffe stehen im Mittelpunkt: offene Tickets von Agents aus dem Team
`Technical`, Tickets mit dem Metadaten-Schlüssel `escalated` und Kommentare
eines Zeitfensters von sieben Tagen. Die Zeitgrenzen tragen die Zone `+00`
und bezeichnen damit unabhängig von der Sitzungszeitzone denselben
Zeitpunkt.

## Aufgaben

### 1. Ausgangspläne

1. Offene Tickets des Teams `Technical`. Frage: Welcher Planknoten liest
   `ticket`? Wie viele Zeilen schätzt der Planner für `status = 'open'`, und
   wie viele liefert der Scan tatsächlich?

   ```sql
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT t.id
   FROM tickets.ticket t
   JOIN tickets.agent a ON a.id = t.agent_id
   WHERE a.team = 'Technical' AND t.status = 'open';
   ```

2. Tickets mit dem Schlüssel `escalated`. Frage: Wie viele Tickets liefert
   der Scan, und wie viele verwirft der Filter?

   ```sql
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT id FROM tickets.ticket WHERE metadata ? 'escalated';
   ```

3. Kommentare aus sieben Tagen. Frage: Wie viele Kommentare fallen in das
   Fenster, und wie viele Blöcke liest der Plan dafür (`shared hit` und
   `read` zusammen)?

   ```sql
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT count(*) FROM tickets.comment
   WHERE created_at >= '2026-01-01 00:00+00'
     AND created_at < '2026-01-08 00:00+00';
   ```

### 2. Indizes anlegen

Lege für jeden Zugriff einen Index an und aktualisiere die Statistiken. Der
Block läuft als eine Ausführung:

```sql
CREATE INDEX IF NOT EXISTS ticket_open_idx
    ON tickets.ticket (agent_id) WHERE status <> 'closed';
CREATE INDEX IF NOT EXISTS ticket_metadata_gin
    ON tickets.ticket USING gin (metadata);
CREATE INDEX IF NOT EXISTS comment_created_brin
    ON tickets.comment USING brin (created_at);
ANALYZE tickets.ticket;
ANALYZE tickets.comment;
```

`ticket_open_idx` ist ein Teilindex: Er enthält nur Zeilen, deren Status
nicht `closed` ist, und passt damit zur Filterbedingung `status = 'open'`.
`ticket_metadata_gin` unterstützt den Existenzoperator `?` auf `jsonb`,
der prüft, ob ein Schlüssel auf oberster Ebene vorkommt.
`comment_created_brin` fasst `created_at` blockweise zusammen.

### 3. Pläne vergleichen

Führe die drei Abfragen aus Aufgabe 1 erneut aus, jede als eigenen Block.

1. Frage: Nutzt der Plan für `Technical` jetzt `ticket_open_idx`? Wie
   ändern sich Laufzeit und gelesene Blöcke gegenüber Aufgabe 1.1?
2. Frage: Nutzt der Plan für `escalated` jetzt `ticket_metadata_gin`?
3. Frage: Nutzt der Plan für das Sieben-Tage-Fenster jetzt
   `comment_created_brin`?

### 4. Größen vergleichen

1. Frage: Wie groß ist `comment_created_brin` im Vergleich zu
   `comment_pkey`?

   ```sql
   SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid))
   FROM pg_stat_user_indexes
   WHERE schemaname = 'tickets' AND relname IN ('agent', 'ticket', 'comment')
   ORDER BY indexrelname;
   ```

2. Frage: Welchen Bruchteil der Tabelle `ticket` belegt `ticket_open_idx`?

   ```sql
   SELECT pg_size_pretty(pg_relation_size('tickets.ticket')) AS ticket_table,
          pg_size_pretty(pg_relation_size('tickets.comment')) AS comment_table;
   ```

### 5. Gegenprobe mit einem Jahr

Frage: Wählt der Planner für ein ganzes Jahr einen anderen Plan als für
sieben Tage?

```sql
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.comment
WHERE created_at >= '2025-01-01 00:00+00'
  AND created_at < '2026-01-01 00:00+00';
```

## Ergebnis prüfen

Die drei neuen Indizes stehen neben den Primärschlüsseln:

```sql
SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid))
FROM pg_stat_user_indexes
WHERE schemaname = 'tickets' AND relname IN ('agent', 'ticket', 'comment')
ORDER BY indexrelname;
```

```text
     indexrelname     | pg_size_pretty
----------------------+----------------
 agent_email_key      | 16 kB
 agent_pkey           | 16 kB
 comment_created_brin | 24 kB
 comment_pkey         | 43 MB
 ticket_metadata_gin  | 6168 kB
 ticket_open_idx      | 296 kB
 ticket_pkey          | 18 MB
(7 rows)
```

## Hinweise

`EXPLAIN` zeigt Zeitstempel in der Zeitzone der Sitzung. Der Kurscluster
läuft mit `TimeZone = Etc/UTC`, die Pläne zeigen die Grenzen deshalb mit
`+00`.

`ticket_metadata_gin` legt auch [Übung 6](../06-moderne-sql/AUFGABE.md)
an. `CREATE INDEX IF NOT EXISTS` überspringt einen vorhandenen Index mit
einem `NOTICE`, deshalb lassen sich Aufgabe 2 und `loesung.sql` mehrfach
ausführen. Die Pläne aus Aufgabe 1 zeigen die Ausgangslage nur, solange die
drei Indizes noch fehlen.

## Auflösung

Die Pläne stammen vom Kurscluster `trainer-pg`, PostgreSQL 18.6, Rolle
`app`, nach Übung 1 in Kursreihenfolge. Die Ausschnitte zeigen die Zeilen,
auf die es ankommt. Die Laufzeiten schwankten zwischen zwei Ausführungen
desselben Plans um mehr als den Faktor 1,5; die Zeilen- und Blockzahlen
blieben gleich. `shared_buffers` ist auf dem Kurscluster 32 MB groß, fast
jeder Block kommt deshalb als `read` aus dem Betriebssystem.

### Aufgabe 1

```text
 Gather (actual time=0.501..2939.781 rows=3784.00 loops=1)
   Buffers: shared hit=1221 read=18555
   ->  Hash Join (rows=1753 actual rows=1261.33 loops=3)
         ->  Parallel Seq Scan on ticket t (rows=5478 actual rows=4381.33 loops=3)
               Filter: (status = 'open'::text)
               Rows Removed by Filter: 262285

 Seq Scan on ticket (rows=92942 actual rows=96231.00 loops=1)
   Filter: (metadata ? 'escalated'::text)
   Rows Removed by Filter: 703769
   Buffers: shared hit=1314 read=18459

 Finalize Aggregate (actual rows=1.00 loops=1)
   Buffers: shared read=26641
   ->  Parallel Seq Scan on comment (rows=8434 actual rows=6321.00 loops=3)
         Filter: ((created_at >= '2026-01-01 00:00:00+00'::...) AND ...)
         Rows Removed by Filter: 660572
```

Alle drei Zugriffe lesen die Tabelle vollständig. Bei einem parallelen Plan
gelten Schätzung und `actual rows` je Prozess, `loops=3` sind der
Leader und zwei Worker. Der Scan auf `ticket` schätzt 5478 offene Tickets
je Prozess und findet 4381, zusammen 13144. Nach dem Join mit den 16
Agents aus `Technical` bleiben 3784. `escalated` tragen 96231 Tickets,
geschätzt waren 92942. Das Sieben-Tage-Fenster enthält 3 × 6321 = 18963
Kommentare, gelesen werden dafür alle 26641 Blöcke von `comment`.

### Aufgabe 3

```text
 Hash Join (actual time=7.018..2207.499 rows=3784.00 loops=1)
   Buffers: shared hit=430 read=16562 written=524
   ->  Bitmap Heap Scan on ticket t (rows=12347 actual rows=13144.00 loops=1)
         Recheck Cond: (status <> 'closed'::text)
         Filter: (status = 'open'::text)
         Rows Removed by Filter: 26540
         Heap Blocks: exact=16955
         ->  Bitmap Index Scan on ticket_open_idx (actual rows=39684.00)
               Buffers: shared read=36

 Bitmap Heap Scan on ticket (rows=98318 actual rows=96231.00 loops=1)
   Recheck Cond: (metadata ? 'escalated'::text)
   Heap Blocks: exact=19213
   Buffers: shared hit=1 read=19230 written=3
   ->  Bitmap Index Scan on ticket_metadata_gin (actual rows=96231.00)
         Buffers: shared hit=1 read=17

 Finalize Aggregate (actual rows=1.00 loops=1)
   Buffers: shared read=26641
   ->  Parallel Seq Scan on comment (rows=7932 actual rows=6321.00 loops=3)
```

`ticket_open_idx` und `ticket_metadata_gin` erscheinen als
`Bitmap Index Scan`. Der Index selbst ist schnell gelesen: 36 und 18
Blöcke. Danach holt der `Bitmap Heap Scan` die Zeilen aus der Tabelle, und
die liegen verstreut. Die 39684 nicht geschlossenen Tickets verteilen sich
auf 16955 der 19773 Seiten von `ticket`, die 96231 Tickets mit
`escalated` auf 19213. Der Plan für `Technical` liest deshalb 16992 statt
19776 Blöcke, der Plan für `escalated` 19231 statt 19773. Ein Index spart
Blöcke erst, wenn die passenden Zeilen auf wenigen Seiten liegen oder nur
ein kleiner Teil der Tabelle gesucht ist.

`comment_created_brin` bleibt ungenutzt, der Plan zeigt weiterhin einen
`Parallel Seq Scan on comment`. Ein BRIN-Index hält je Block-Bereich nur
Minimum und Maximum der indizierten Spalte fest. Er hilft nur, wenn
benachbarte Zeilen ähnliche Werte tragen, wenn also die physische
Reihenfolge der Tabelle mit der Spalte korreliert:

```sql
SELECT attname, correlation FROM pg_stats
WHERE schemaname = 'tickets' AND tablename = 'comment' AND attname = 'created_at';
```

Auf dem Kurscluster liefert die Abfrage `-0.0056639384`, praktisch keine
Korrelation. Ein Kommentar liegt physisch in der Reihenfolge seines Tickets.
Sein `created_at` richtet sich nach dem `created_at` des Tickets, und das
wählt `setup.sql` unabhängig von der `id` zufällig aus 730 Tagen.

Mit `SET enable_seqscan = off;` in einem eigenen Block davor wählt der
Planner den BRIN-Index. Der Plan zeigt dann `Heap Blocks: lossy` von
zusammen 26641 Blöcken, also die ganze Tabelle, und
`Rows Removed by Index Recheck: 660572` je Prozess. Der Index schließt
keinen einzigen Block aus. `RESET enable_seqscan;` stellt den Planner
danach zurück.

Die Kosten beider Pläne liegen nahe beieinander. Nach einem erneuten
`ANALYZE` wählte der Planner in einem von vier Läufen auf dem Kurscluster
von sich aus `Parallel Bitmap Heap Scan on comment` über
`comment_created_brin`. Die zufällige Stichprobe von `ANALYZE` verschiebt
die Schätzung ein wenig. Beide Pläne lesen dieselben 26641 Blöcke.

### Aufgabe 4

```text
     indexrelname     | pg_size_pretty
----------------------+----------------
 comment_created_brin | 24 kB
 comment_pkey         | 43 MB
 ticket_metadata_gin  | 6168 kB
 ticket_open_idx      | 296 kB
 ticket_pkey          | 18 MB

 ticket_table | comment_table
--------------+---------------
 154 MB       | 208 MB
```

`comment_created_brin` ist mit 24 kB der kleinste Index, `comment_pkey`
belegt 43 MB. `ticket_open_idx` enthält nur die rund 40000 nicht
geschlossenen Tickets und belegt 296 kB, etwa 0,2 % der Tabelle. Ein
BRIN-Index lohnt sich für Spalten, die mit der Einfügereihenfolge
korrelieren, etwa eine echte Ereigniszeit in einer Tabelle, in die nur
angehängt wird.

### Aufgabe 5

```text
 Finalize Aggregate
   ->  Gather
         Workers Planned: 2
         ->  Partial Aggregate
               ->  Parallel Seq Scan on comment
                     Filter: ((created_at >= '2025-01-01 00:00:00+00'::timestamp with time zone) AND (created_at < '2026-01-01 00:00:00+00'::timestamp with time zone))
```

Der Plan bleibt ein `Parallel Seq Scan`. Das Jahr 2025 enthält 997687 der
2000678 Kommentare, rund die Hälfte; ein Index brächte hier keinen Vorteil.
Schon beim engen Fenster schließt `comment_created_brin` keinen Block aus.
