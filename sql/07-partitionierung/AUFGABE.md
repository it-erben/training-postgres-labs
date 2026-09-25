# SQL-Übung 7: Partitionierung

## Ziel

Du legst eine nach Quartalen partitionierte Kopie der Tabelle `ticket` an,
prüfst im Ausführungsplan, welche Partitionen eine Abfrage liest, und
trennst eine Partition ab und hängst sie wieder an. `ticket` selbst bleibt
unverändert.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet, `ticket` enthält 800000 Zeilen. Die Spalte `created_at` liegt
zwischen dem 21.08.2024 und dem 21.08.2026 (UTC). Die Übung setzt keine
andere Übung voraus.

Arbeite im Query Tool mit `Auto commit` an und `Auto rollback on error` aus.
Jeder Codeblock ist eine Ausführung, wie in Übung 0 beschrieben.
Partitionsgrenzen vom Typ `timestamptz` hängen von der Sitzungszeitzone ab,
wenn sie ohne Zeitzone geschrieben sind. Schreibe die Grenzen deshalb mit
`+00`, etwa `'2024-07-01 00:00+00'`.

## Aufgaben

1. Lege `tickets.ticket_partitioned` mit denselben acht Spalten wie
   `ticket` an: `id`, `agent_id`, `subject`, `status`, `priority`,
   `metadata`, `created_at`, `closed_at`. `id` ist wie in `ticket` eine
   Identity-Spalte (`GENERATED ALWAYS AS IDENTITY`). Partitioniere mit
   `PARTITION BY RANGE (created_at)` und lege `PRIMARY KEY (id, created_at)`
   an. Probiere vorher `PRIMARY KEY (id)` und lies die Fehlermeldung.
2. Lege neun Quartalspartitionen an, von `ticket_2024q3` (ab 01.07.2024) bis
   `ticket_2026q3` (bis 01.10.2026), dazu eine DEFAULT-Partition
   `ticket_default`:

   ```sql
   CREATE TABLE tickets.ticket_2024q3 PARTITION OF tickets.ticket_partitioned
       FOR VALUES FROM ('2024-07-01 00:00+00') TO ('2024-10-01 00:00+00');
   ```

3. Kopiere alle Tickets mit ihren IDs. Weil `id` eine Identity-Spalte mit
   `ALWAYS` ist, braucht das `INSERT` die Klausel `OVERRIDING SYSTEM VALUE`:

   ```sql
   INSERT INTO tickets.ticket_partitioned
       (id, agent_id, subject, status, priority, metadata, created_at, closed_at)
   OVERRIDING SYSTEM VALUE
   SELECT id, agent_id, subject, status, priority, metadata, created_at, closed_at
   FROM tickets.ticket;
   ```

   Setze danach die Identity mit `setval` auf `max(id)`, damit die nächste
   erzeugte ID 800001 ist. Den Namen der Sequenz liefert
   `pg_get_serial_sequence('tickets.ticket_partitioned', 'id')`. Führe
   anschließend `ANALYZE tickets.ticket_partitioned;` aus.
4. Vergleiche zwei Abfragen, die beide die Tickets des ersten Quartals 2025
   zählen. Die erste filtert direkt auf `created_at`:

   ```sql
   EXPLAIN (COSTS OFF)
   SELECT count(*) FROM tickets.ticket_partitioned
   WHERE created_at >= '2025-01-01 00:00+00' AND created_at < '2025-04-01 00:00+00';
   ```

   Die zweite bestimmt das Quartal über eine Funktion und hat damit keinen
   Filter auf `created_at` selbst:

   ```sql
   EXPLAIN (COSTS OFF)
   SELECT count(*) FROM tickets.ticket_partitioned
   WHERE date_trunc('quarter', created_at AT TIME ZONE 'UTC') = timestamp '2025-01-01';
   ```

   Zähle, wie viele Partitionen jeder Plan nennt. Führe beide Abfragen auch
   ohne `EXPLAIN` aus und vergleiche die Zahlen.
5. Trenne `ticket_2024q3` mit `ALTER TABLE ... DETACH PARTITION` ab und zähle
   ihre Zeilen als eigenständige Tabelle. Lege dann auf `ticket_2024q3`
   einen `CHECK` an, der genau die Quartalsgrenzen beschreibt, und hänge die
   Tabelle mit `ATTACH PARTITION` und denselben Grenzen wieder an. Entferne
   den `CHECK` danach.

## Ergebnis prüfen

Zeilen je Partition:

```sql
SELECT tableoid::regclass AS partition, count(*) AS row_count
FROM tickets.ticket_partitioned
GROUP BY tableoid ORDER BY tableoid::regclass::text;
```

Die Abfrage listet die neun Quartalspartitionen mit ihren Zeilen. Die Summe
ist 800000; die leere DEFAULT-Partition erscheint nicht:

```text
       partition       | row_count
-----------------------+-----------
 tickets.ticket_2024q3 |     45093
 tickets.ticket_2024q4 |    101272
 tickets.ticket_2025q1 |     98588
 tickets.ticket_2025q2 |     99128
 tickets.ticket_2025q3 |    100192
 tickets.ticket_2025q4 |    101052
 tickets.ticket_2026q1 |     98924
 tickets.ticket_2026q2 |     99776
 tickets.ticket_2026q3 |     55975
(9 rows)
```

Zahl der Partitionen:

```sql
SELECT count(*) AS partitions FROM pg_inherits
WHERE inhparent = 'tickets.ticket_partitioned'::regclass;
```

Sie zählt zehn, die DEFAULT-Partition eingeschlossen:

```text
 partitions
------------
         10
(1 row)
```

Der Plan für das erste Quartal 2025:

```sql
EXPLAIN (COSTS OFF)
SELECT count(*) FROM tickets.ticket_partitioned
WHERE created_at >= '2025-01-01 00:00+00' AND created_at < '2025-04-01 00:00+00';
```

Er nennt nur `ticket_2025q1`. Die Grenzen erscheinen in der Zeitzone der
Sitzung, auf dem Kurscluster UTC:

```text
                                                                         QUERY PLAN
------------------------------------------------------------------------------------------------------------------------------------------------------------
 Aggregate
   ->  Seq Scan on ticket_2025q1 ticket_partitioned
         Filter: ((created_at >= '2025-01-01 00:00:00+00'::timestamp with time zone) AND (created_at < '2025-04-01 00:00:00+00'::timestamp with time zone))
(3 rows)
```

## Hinweise

`PRIMARY KEY (id)` scheitert mit
`unique constraint on partitioned table must include all partitioning columns`.
PostgreSQL prüft Eindeutigkeit nur innerhalb einer Partition, deshalb muss
`created_at` im Schlüssel stehen. Eindeutig ist damit das Paar
`(id, created_at)`. Dieselbe `id` dürfte in zwei Quartalen vorkommen.

In Aufgabe 4 nennt der erste Plan eine Partition, der zweite alle zehn. Die
Partitionsauswahl vergleicht Bedingungen direkt mit den Grenzen. Ein
Ausdruck wie `date_trunc(...)` um die Spalte verhindert sie, obwohl beide
Abfragen 98588 Tickets zählen. Welcher Zugriff innerhalb der Partition
erscheint, etwa `Seq Scan` oder `Index Only Scan`, hängt vom Zustand der
Tabelle ab. Es kommt auf die Zahl der Partitionen an, die der Plan nennt.

In Aufgabe 5 zählt die abgetrennte Tabelle 45093 Zeilen. Mit dem passenden
`CHECK` muss `ATTACH` die Zeilen nicht erneut prüfen. Das lässt sich
sichtbar machen, wenn vor dem `ATTACH` in derselben Transaktion
`SET LOCAL client_min_messages = debug1;` steht. `Messages` zeigt dann
`partition constraint for table "ticket_2024q3" is implied by existing constraints`.
Ohne `CHECK` erscheint stattdessen `verifying table "ticket_2024q3"`.
`DETACH ... CONCURRENTLY` ist hier nicht möglich, weil eine DEFAULT-Partition
existiert.

Ein Fremdschlüssel wie `comment.ticket_id` kann nicht auf
`ticket_partitioned (id)` zeigen, weil es dort keinen eindeutigen Schlüssel
über `id` allein gibt. Er müsste `created_at` mitführen. Deshalb bleibt
`ticket` in dieser Übung die Tabelle, auf die `comment` verweist.

`loesung.sql` entfernt zu Beginn `ticket_partitioned` und eine eventuell
abgetrennte `ticket_2024q3` und lässt sich deshalb mehrfach ausführen.
