# SQL-Übung 6: Constraints

## Ziel

Du validierst eine nachträglich angelegte Prüfregel ohne Tabellensperre,
findest einen Fremdschlüssel ohne unterstützenden Index und baust mit einem
Exclusion Constraint einen Bereitschaftsplan ohne überlappende Zeiträume.
Zum Schluss beobachtest du, wann ein aufschiebbarer Fremdschlüssel geprüft
wird.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus: Aufgabe 4 und 5
lösen absichtlich Fehler aus.

Entferne zu Beginn die Übungsobjekte, damit sich die Übung wiederholen
lässt. `ticket_metadata_gin` aus den Übungen 2 und 5 bleibt davon
unberührt:

```sql
DROP TABLE IF EXISTS tickets.ticket_referenz;
DROP TABLE IF EXISTS tickets.bereitschaft;
ALTER TABLE tickets.ticket DROP CONSTRAINT IF EXISTS ticket_priority_check;
DROP INDEX IF EXISTS tickets.comment_parent_idx;
```

## Aufgaben

1. Lege für `tickets.ticket.priority` eine Prüfregel auf den Wertebereich 1
   bis 4 an, zuerst ungeprüft, dann validiert:

   ```sql
   ALTER TABLE tickets.ticket
       ADD CONSTRAINT ticket_priority_check
       CHECK (priority BETWEEN 1 AND 4) NOT VALID;
   ALTER TABLE tickets.ticket VALIDATE CONSTRAINT ticket_priority_check;
   ```

   `NOT VALID` nimmt beim Anlegen nur eine kurze Tabellensperre und prüft
   den Bestand nicht. Neue und geänderte Zeilen unterliegen der Regel ab
   diesem Zeitpunkt bereits. `VALIDATE CONSTRAINT` liest anschließend den
   vorhandenen Bestand in einem separaten Schritt mit einer schwächeren
   Sperre, die parallele Lesezugriffe zulässt.

2. Finde mit `pg_constraint` und `pg_index` alle Fremdschlüssel im Schema
   `tickets`, deren erste Spalte keinen Index anführt:

   ```sql
   SELECT c.conrelid::regclass AS tabelle, c.conname
   FROM pg_constraint c
   WHERE c.contype = 'f'
     AND c.connamespace = 'tickets'::regnamespace
     AND NOT EXISTS (
         SELECT 1 FROM pg_index i
         WHERE i.indrelid = c.conrelid
           AND i.indkey[0] = c.conkey[1]
     )
   ORDER BY c.conname;
   ```

   Referenzlauf:

   ```text
       tabelle      |        conname         
   -----------------+------------------------
    tickets.comment | comment_parent_id_fkey
    tickets.comment | comment_ticket_id_fkey
    tickets.ticket  | ticket_agent_id_fkey
   (3 rows)
   ```

   `comment.parent_id` trägt keinen Index, obwohl die rekursive Abfrage aus
   [Übung 5](../05-moderne-sql/AUFGABE.md) genau diese Spalte für jeden
   Rekursionsschritt nachschlägt. Lege den fehlenden Index an:

   ```sql
   CREATE INDEX comment_parent_idx ON tickets.comment (parent_id);
   ```

3. Lege `tickets.bereitschaft(agent_id, zeitraum)` an. Kein Agent darf zwei
   sich überlappende Bereitschaftszeiten haben:

   ```sql
   CREATE EXTENSION IF NOT EXISTS btree_gist;

   CREATE TABLE tickets.bereitschaft (
       agent_id bigint REFERENCES tickets.agent(id),
       zeitraum tstzrange NOT NULL,
       EXCLUDE USING gist (agent_id WITH =, zeitraum WITH &&)
   );
   ```

   `EXCLUDE USING gist` verbietet zwei Zeilen, für die alle genannten
   Operatoren gleichzeitig `TRUE` ergeben: gleicher `agent_id`-Wert
   (`=`) und überlappender Zeitraum (`&&`). `btree_gist` liefert den
   GiST-Operatorklassen für `=` auf `bigint`, den `tstzrange` selbst
   schon mitbringt.

4. Füge zwei angrenzende Bereitschaftszeiten für Agent 1 ein und danach
   eine Zeit, die beide überlappt:

   ```sql
   INSERT INTO tickets.bereitschaft (agent_id, zeitraum) VALUES
       (1, tstzrange('2026-09-01 00:00+00', '2026-09-08 00:00+00', '[)')),
       (1, tstzrange('2026-09-08 00:00+00', '2026-09-15 00:00+00', '[)'));

   INSERT INTO tickets.bereitschaft (agent_id, zeitraum)
   VALUES (1, tstzrange('2026-09-05 00:00+00', '2026-09-10 00:00+00', '[)'));
   ```

   Die ersten beiden Zeiten teilen keinen gemeinsamen Zeitpunkt: Das
   Intervall `[)` schließt die obere Grenze aus, `2026-09-08 00:00+00`
   gehört nur zum zweiten Zeitraum. Beide `INSERT`-Anweisungen laufen
   durch. Die dritte Zeile überlappt beide vorhandenen Zeiträume und
   scheitert mit SQLSTATE `23P01`
   (`conflicting key value violates exclusion constraint`).

5. Lege eine Tabelle mit einem aufschiebbaren Fremdschlüssel an und
   beobachte, wann PostgreSQL ihn prüft:

   ```sql
   CREATE TABLE tickets.ticket_referenz (
       id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
       ticket_id bigint NOT NULL
           REFERENCES tickets.ticket(id) DEFERRABLE INITIALLY DEFERRED
   );

   BEGIN;
   INSERT INTO tickets.ticket_referenz (ticket_id) VALUES (9999999);
   -- INSERT 0 1, ohne Fehler: Ticket 9999999 existiert nicht.
   COMMIT;
   -- ERROR: 23503: insert or update on table "ticket_referenz" violates
   -- foreign key constraint "ticket_referenz_ticket_id_fkey"
   ```

   `DEFERRABLE INITIALLY DEFERRED` verschiebt die Prüfung auf das Ende der
   Transaktion. Das `INSERT` selbst meldet keinen Fehler, obwohl die
   referenzierte Zeile fehlt. Erst `COMMIT` wertet den Fremdschlüssel aus
   und bricht die gesamte Transaktion mit SQLSTATE `23503` ab; die
   eingefügte Zeile bleibt nicht bestehen.

## Ergebnis prüfen

```sql
SELECT conname, contype, convalidated FROM pg_constraint
WHERE conrelid IN ('tickets.ticket'::regclass, 'tickets.bereitschaft'::regclass)
  AND contype IN ('c', 'x') ORDER BY conname;
SELECT agent_id, zeitraum FROM tickets.bereitschaft ORDER BY agent_id, lower(zeitraum);
```

Referenzlauf:

```text
               conname               | contype | convalidated 
-------------------------------------+---------+--------------
 bereitschaft_agent_id_zeitraum_excl | x       | t
 ticket_priority_check               | c       | t
(2 rows)

 agent_id |                      zeitraum                       
----------+-----------------------------------------------------
        1 | ["2026-09-01 00:00:00+00","2026-09-08 00:00:00+00")
        1 | ["2026-09-08 00:00:00+00","2026-09-15 00:00:00+00")
(2 rows)
```

`convalidated = t` gilt für beide Regeln: `ticket_priority_check`, weil
Aufgabe 1 sie ausdrücklich validiert hat, `bereitschaft_..._excl`, weil ein
beim `CREATE TABLE` angelegter Constraint nie im Zustand `NOT VALID`
beginnt. Nur ein nachträglich per `ALTER TABLE ... ADD CONSTRAINT`
angefügter Constraint kennt diesen Zwischenzustand.

## Hinweise

`pg_constraint.conkey` und `pg_index.indkey` speichern Spaltenpositionen,
nicht Spaltennamen. `conkey` ist ein bei 1 beginnendes Array, `indkey` ein
bei 0 beginnendes `int2vector`; deshalb vergleicht die Abfrage in Aufgabe 2
`conkey[1]` mit `indkey[0]`. Ein zusammengesetzter Fremdschlüssel bräuchte
einen Vergleich über alle Positionen; hier reicht die erste Spalte, weil
jeder betroffene Fremdschlüssel nur eine Spalte umfasst.

Ein Index auf einer Fremdschlüsselspalte entsteht in PostgreSQL nicht
automatisch, anders als beim Primärschlüssel selbst. Ohne ihn braucht ein
`DELETE` auf der Elterntabelle für die Fremdschlüsselprüfung einen
sequenziellen Scan der Kindtabelle, und rekursive Abfragen wie in Übung 5
lesen bei jedem Rekursionsschritt ohne Index die gesamte Tabelle.

`EXCLUDE USING gist` erweitert die Idee eines Unique-Constraints von
Gleichheit auf beliebige Operatoren. Statt "keine zwei Zeilen mit
gleichem Wert" gilt hier "keine zwei Zeilen, für die Gleichheit auf
`agent_id` und Überlappung auf `zeitraum` gleichzeitig zutreffen".

`loesung.sql` entfernt zu Beginn `tickets.ticket_referenz`,
`tickets.bereitschaft`, `ticket_priority_check` und `comment_parent_idx`
und lässt sich deshalb mehrfach ausführen.
