# SQL-Übung 4: Indizes und EXPLAIN

## Ziel

Du liest drei Ausführungspläne mit `EXPLAIN (ANALYZE, BUFFERS)`, legst für
jeden Zugriff einen passenden Index an und vergleichst die Pläne danach
erneut. Zum Schluss prüfst du die Indexgrößen und eine Gegenprobe mit einem
breiteren Zeitfenster. Nach jeder Abfrage liest du einen Wert aus deiner
eigenen Ausgabe ab. Ein eingeklappter Block darunter enthält den Plan eines
Referenzlaufs mit einer Erklärung, was die Werte bedeuten.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus. Jeder Codeblock
ist eine Ausführung, wie in Übung 0 beschrieben.

Führe jede Abfrage zuerst selbst aus und beantworte die Frage unter
**Ablesen** aus deiner Ausgabe. Klappe erst danach den Block
"Referenzausgabe und Erklärung" auf und vergleiche.

Drei Zugriffe stehen im Mittelpunkt: offene Tickets von Agents aus dem Team
`Technical`, Tickets mit dem Metadaten-Schlüssel `escalated` und Kommentare
eines Zeitfensters von sieben Tagen. Die Zeitgrenzen tragen die Zone `+00`
und bezeichnen damit unabhängig von der Sitzungszeitzone denselben
Zeitpunkt.

Die Referenzpläne stammen vom Kurscluster `trainer-pg`, PostgreSQL 18.6,
Rolle `app`, nach `setup.sql` und [Übung 1](../01-architektur/AUFGABE.md).
Übung 1 vergrößert `ticket` in Aufgabe 5 von 19293 auf 19773 Seiten. Ohne
Übung 1 weichen deshalb die Blockzahlen für `ticket` ab. Die Übungen 2 und 3
ändern an `ticket` nur die Tickets 1 und 2. Die Ausschnitte zeigen die
Zeilen, auf die es ankommt.

Planknoten und `actual rows` stimmen in jeder Umgebung mit dem Referenzlauf
überein, weil `setup.sql` immer denselben Datenbestand erzeugt. Die
geschätzten `rows` beruhen auf einer Stichprobe von `ANALYZE` und schwanken
leicht. Laufzeiten und die Aufteilung der Blöcke auf `shared hit` und `read`
hängen von der Umgebung ab. Auf dem Kurscluster schwankte die Laufzeit
desselben Plans zwischen zwei Ausführungen um mehr als den Faktor 1,5. Dort
ist `shared_buffers` 32 MB groß, fast jeder Block kommt deshalb als `read`
aus dem Betriebssystem. In einem lokalen Container brauchte der Plan aus
Aufgabe 1.1 rund 20 ms statt 2940 ms.

## Aufgaben

### 1. Ausgangspläne

1. Offene Tickets des Teams `Technical`:

   ```sql
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT t.id
   FROM tickets.ticket t
   JOIN tickets.agent a ON a.id = t.agent_id
   WHERE a.team = 'Technical' AND t.status = 'open';
   ```

   **Ablesen:** Welcher Knoten liest `ticket`, und wie viele Zeilen schätzt
   er für `status = 'open'` im Vergleich zu `actual rows`?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    Gather (actual time=0.501..2939.781 rows=3784.00 loops=1)
      Buffers: shared hit=1221 read=18555
      ->  Hash Join (rows=1753 actual rows=1261.33 loops=3)
            ->  Parallel Seq Scan on ticket t (rows=5478 actual rows=4381.33 loops=3)
                  Filter: (status = 'open'::text)
                  Rows Removed by Filter: 262285
   ```

   **Was das Ergebnis zeigt:** `Parallel Seq Scan on ticket t` liest die
   ganze Tabelle. Bei einem parallelen Plan gelten Schätzung und
   `actual rows` je Prozess, `loops=3` sind der Leader und zwei Worker. Der
   Scan schätzt 5478 offene Tickets je Prozess und findet 4381, zusammen
   13144. Nach dem Join mit den 16 Agents aus `Technical` bleiben 3784. Die
   1221 + 18555 = 19776 Blöcke sind alle 19773 Seiten von `ticket` und die
   Blöcke von `agent`.

   Deine Laufzeit und die Aufteilung auf `hit` und `read` weichen ab. Die
   Summe der Blöcke weicht nur ohne Übung 1 ab. Den `Parallel Seq Scan` und
   die 3784 Zeilen zeigt jeder Lauf.

   </details>

2. Tickets mit dem Schlüssel `escalated`:

   ```sql
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT id FROM tickets.ticket WHERE metadata ? 'escalated';
   ```

   **Ablesen:** Wie viele Zeilen liefert der Scan (`actual rows`), und wie
   viele verwirft der Filter (`Rows Removed by Filter`)?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    Seq Scan on ticket (rows=92942 actual rows=96231.00 loops=1)
      Filter: (metadata ? 'escalated'::text)
      Rows Removed by Filter: 703769
      Buffers: shared hit=1314 read=18459
   ```

   **Was das Ergebnis zeigt:** Der `Seq Scan` prüft alle 800000 Tickets.
   96231 tragen den Schlüssel `escalated`, die übrigen 703769 verwirft der
   Filter. Geschätzt waren 92942. Mit 1314 + 18459 = 19773 Blöcken liest der
   Plan jede Seite von `ticket` genau einmal.

   Die beiden Zeilenzahlen stimmen in jeder Umgebung, die Schätzung schwankt
   mit der Stichprobe von `ANALYZE`.

   </details>

3. Kommentare aus sieben Tagen:

   ```sql
   EXPLAIN (ANALYZE, BUFFERS)
   SELECT count(*) FROM tickets.comment
   WHERE created_at >= '2026-01-01 00:00+00'
     AND created_at < '2026-01-08 00:00+00';
   ```

   **Ablesen:** Wie viele Blöcke liest der Plan? Zähle `shared hit` und
   `read` in der obersten `Buffers`-Zeile zusammen.

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    Finalize Aggregate (actual rows=1.00 loops=1)
      Buffers: shared read=26641
      ->  Parallel Seq Scan on comment (rows=8434 actual rows=6321.00 loops=3)
            Filter: ((created_at >= '2026-01-01 00:00:00+00'::...) AND ...)
            Rows Removed by Filter: 660572
   ```

   **Was das Ergebnis zeigt:** Das Fenster enthält 3 × 6321 = 18963 der
   2000678 Kommentare, weniger als 1 %. Gelesen werden dafür alle 26641
   Blöcke von `comment`, der Filter verwirft je Prozess 660572 Zeilen.

   Die Summe von 26641 Blöcken gilt in jeder Umgebung, Übung 1 ändert
   `comment` nicht. Wie viele davon als `hit` zählen, hängt vom Cache ab.

   </details>

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

1. Die Abfrage für `Technical` aus Aufgabe 1.1.

   **Ablesen:** Welcher Knoten nutzt `ticket_open_idx`, und wie viele Blöcke
   liest der Plan jetzt im Vergleich zu Aufgabe 1.1?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

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
   ```

   **Was das Ergebnis zeigt:** `ticket_open_idx` erscheint als
   `Bitmap Index Scan`. Der Index selbst ist mit 36 Blöcken schnell gelesen.
   Er liefert die 39684 nicht geschlossenen Tickets, der `Filter` behält
   davon die 13144 offenen. Danach holt der `Bitmap Heap Scan` die Zeilen aus
   der Tabelle, und die liegen verstreut: auf 16955 der 19773 Seiten von
   `ticket`. Der Plan liest deshalb 430 + 16562 = 16992 Blöcke statt 19776.

   Die Laufzeit eignet sich für diesen Vergleich schlecht. Auf dem
   Kurscluster sank sie von 2940 auf 2207 ms, innerhalb der Schwankung
   zwischen zwei Ausführungen. In einem lokalen Container stieg sie von 20
   auf 107 ms. Die beiden Bitmap-Knoten und die Zeilenzahlen zeigt jeder
   Lauf, die `Heap Blocks` weichen ohne Übung 1 ab.

   </details>

2. Die Abfrage für `escalated` aus Aufgabe 1.2.

   **Ablesen:** Wie viele Blöcke liest der `Bitmap Index Scan` auf
   `ticket_metadata_gin`, und auf wie viele Seiten greift der
   `Bitmap Heap Scan` darüber zu (`Heap Blocks`)?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    Bitmap Heap Scan on ticket (rows=98318 actual rows=96231.00 loops=1)
      Recheck Cond: (metadata ? 'escalated'::text)
      Heap Blocks: exact=19213
      Buffers: shared hit=1 read=19230 written=3
      ->  Bitmap Index Scan on ticket_metadata_gin (actual rows=96231.00)
            Buffers: shared hit=1 read=17
   ```

   **Was das Ergebnis zeigt:** Der GIN-Index ist mit 1 + 17 = 18 Blöcken
   gelesen. Die 96231 Tickets mit `escalated` verteilen sich aber auf 19213
   Seiten, fast die ganze Tabelle. Der Plan liest 19231 statt 19773 Blöcke.
   Ein Index spart Blöcke erst, wenn die passenden Zeilen auf wenigen Seiten
   liegen oder nur ein kleiner Teil der Tabelle gesucht ist.

   Die Zahl der `Heap Blocks` hängt davon ab, wo die Zeilen liegen, und
   weicht ohne Übung 1 ab. Die Knoten und die 96231 Zeilen bleiben gleich.

   </details>

3. Die Abfrage für das Sieben-Tage-Fenster aus Aufgabe 1.3.

   **Ablesen:** Taucht `comment_created_brin` im Plan auf?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    Finalize Aggregate (actual rows=1.00 loops=1)
      Buffers: shared read=26641
      ->  Parallel Seq Scan on comment (rows=7932 actual rows=6321.00 loops=3)
   ```

   **Was das Ergebnis zeigt:** `comment_created_brin` bleibt ungenutzt, der
   Plan zeigt weiterhin `Parallel Seq Scan on comment` und liest alle 26641
   Blöcke. Ein BRIN-Index hält je Block-Bereich nur Minimum und Maximum der
   indizierten Spalte fest. Er hilft nur, wenn benachbarte Zeilen ähnliche
   Werte tragen, wenn also die physische Reihenfolge der Tabelle mit der
   Spalte korreliert. Schritt 4 prüft das.

   Die Kosten beider Pläne liegen nahe beieinander. Nach einem erneuten
   `ANALYZE` wählte der Planner in einem von vier Läufen auf dem Kurscluster
   von sich aus `Parallel Bitmap Heap Scan on comment` über
   `comment_created_brin`. Die zufällige Stichprobe von `ANALYZE` verschiebt
   die Schätzung ein wenig. Beide Pläne lesen dieselben 26641 Blöcke.

   </details>

4. Die Korrelation von `created_at` mit der physischen Reihenfolge der
   Zeilen:

   ```sql
   SELECT attname, correlation FROM pg_stats
   WHERE schemaname = 'tickets' AND tablename = 'comment' AND attname = 'created_at';
   ```

   **Ablesen:** Welchen Wert zeigt `correlation`?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
     attname   |  correlation
   ------------+---------------
    created_at | -0.0056639384
   (1 row)
   ```

   **Was das Ergebnis zeigt:** `correlation` liegt zwischen -1 und 1. Ein
   Wert nahe 1 oder -1 heißt, dass die Zeilen physisch fast nach der Spalte
   sortiert liegen. Um 0 besteht praktisch kein Zusammenhang. Ein Kommentar
   liegt physisch in der Reihenfolge seines Tickets. Sein `created_at`
   richtet sich nach dem `created_at` des Tickets, und das wählt `setup.sql`
   unabhängig von der `id` zufällig aus 730 Tagen. Den genauen Wert bestimmt
   die Stichprobe von `ANALYZE`, ein lokaler Lauf zeigte `0.01495202`.

   Mit `SET enable_seqscan = off;` in einem eigenen Block vor der Abfrage
   aus Schritt 3 wählt der Planner den BRIN-Index. Der Plan zeigt dann
   `Heap Blocks: lossy` von zusammen 26641 Blöcken, also die ganze Tabelle,
   und `Rows Removed by Index Recheck: 660572` je Prozess. Der Index schließt
   keinen einzigen Block aus. `RESET enable_seqscan;` stellt den Planner
   danach zurück.

   </details>

### 4. Größen vergleichen

1. Größe der Indizes:

   ```sql
   SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid))
   FROM pg_stat_user_indexes
   WHERE schemaname = 'tickets' AND relname IN ('agent', 'ticket', 'comment')
   ORDER BY indexrelname;
   ```

   **Ablesen:** Wie groß ist `comment_created_brin`, wie groß `comment_pkey`?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

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

   **Was das Ergebnis zeigt:** `comment_created_brin` ist mit 24 kB der
   kleinste Index, `comment_pkey` belegt für dieselbe Tabelle 43 MB. Der
   B-Baum hält je Zeile einen Eintrag, der BRIN-Index je Bereich von 128
   Seiten nur Minimum und Maximum. Ein BRIN-Index lohnt sich für Spalten, die
   mit der Einfügereihenfolge korrelieren, etwa eine echte Ereigniszeit in
   einer Tabelle, in die nur angehängt wird. `ticket_open_idx` enthält nur
   die 39684 nicht geschlossenen Tickets.

   Ein lokaler Lauf zeigte dieselben Größen.

   </details>

2. Größe der Tabellen:

   ```sql
   SELECT pg_size_pretty(pg_relation_size('tickets.ticket')) AS ticket_table,
          pg_size_pretty(pg_relation_size('tickets.comment')) AS comment_table;
   ```

   **Ablesen:** Welchen Bruchteil der Tabelle `ticket` belegt
   `ticket_open_idx` aus Schritt 1?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    ticket_table | comment_table
   --------------+---------------
    154 MB       | 208 MB
   (1 row)
   ```

   **Was das Ergebnis zeigt:** `ticket_open_idx` belegt 296 kB, etwa 0,2 %
   der 154 MB von `ticket`. Der Teilindex bleibt klein, weil rund 95 % der
   Tickets geschlossen sind und darin fehlen.

   Ohne Übung 1 ist `ticket` 151 MB groß.

   </details>

### 5. Gegenprobe mit einem Jahr

Dieselbe Zählung für das ganze Jahr 2025. `COSTS OFF` zeigt nur die
Planknoten:

```sql
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.comment
WHERE created_at >= '2025-01-01 00:00+00'
  AND created_at < '2026-01-01 00:00+00';
```

**Ablesen:** Welcher Knoten liest `comment`?

<details>
<summary>Referenzausgabe und Erklärung</summary>

```text
 Finalize Aggregate
   ->  Gather
         Workers Planned: 2
         ->  Partial Aggregate
               ->  Parallel Seq Scan on comment
                     Filter: ((created_at >= '2025-01-01 00:00:00+00'::timestamp with time zone) AND (created_at < '2026-01-01 00:00:00+00'::timestamp with time zone))
```

**Was das Ergebnis zeigt:** Der Plan bleibt ein `Parallel Seq Scan`. Das
Jahr 2025 enthält 997687 der 2000678 Kommentare, rund die Hälfte; ein Index
brächte hier keinen Vorteil. Schon beim engen Fenster schließt
`comment_created_brin` keinen Block aus.

Ohne `ANALYZE` führt `EXPLAIN` die Abfrage nicht aus. Die Ausgabe enthält
weder Laufzeiten noch Blöcke und sieht in jeder Umgebung gleich aus.

</details>

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
einem `NOTICE`, deshalb lassen sich Aufgabe 2 und die Musterlösung
`answers/sql/04-explain/loesung.sql` mehrfach ausführen. Die Pläne aus
Aufgabe 1 zeigen die Ausgangslage nur, solange die drei Indizes noch
fehlen.
