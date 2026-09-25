# SQL-Übung 6: Constraints

## Ziel

Du legst eine Prüfregel nachträglich mit `NOT VALID` an, was die Tabelle
nur kurz mit `ACCESS EXCLUSIVE` sperrt. Danach validierst du sie unter
`SHARE UPDATE EXCLUSIVE`, ohne Lesen und Schreiben zu blockieren. Außerdem
findest du einen Fremdschlüssel ohne unterstützenden Index und baust mit
einem Exclusion Constraint einen Bereitschaftsplan ohne überlappende
Zeiträume. Zum Schluss beobachtest du, wann ein aufschiebbarer
Fremdschlüssel geprüft wird.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus, denn Aufgabe 4
und 5 lösen absichtlich Fehler aus. Für Aufgabe 1 brauchst du zwei
Verbindungen A und B.

Entferne zu Beginn die Übungsobjekte, damit sich die Übung wiederholen
lässt. `ticket_metadata_gin` aus den Übungen 2 und 5 bleibt davon
unberührt:

```sql
DROP TABLE IF EXISTS tickets.ticket_ref;
DROP TABLE IF EXISTS tickets.on_call;
ALTER TABLE tickets.ticket DROP CONSTRAINT IF EXISTS ticket_priority_check;
DROP INDEX IF EXISTS tickets.comment_parent_idx;
```

## Aufgaben

1. Lege für `tickets.ticket.priority` eine Prüfregel auf den Wertebereich 1
   bis 4 an, zuerst ungeprüft, dann validiert. Führe `ADD CONSTRAINT` als
   eigene, sofort bestätigte Anweisung aus, bevor du `VALIDATE CONSTRAINT`
   in einer offenen Transaktion beobachtest. Läuft `ADD CONSTRAINT` in
   derselben Transaktion wie `VALIDATE CONSTRAINT`, bleibt seine kurze,
   aber starke Sperre bis zum `COMMIT` bestehen und verfälscht die
   Beobachtung. Führe in A die beiden folgenden Blöcke nacheinander
   einzeln aus. Als eine Markierung liefe `ADD CONSTRAINT` in der
   Transaktion, die `BEGIN` öffnet. Erster Block in A:

   ```sql
   SET application_name = 'exercise_a';
   ALTER TABLE tickets.ticket
       ADD CONSTRAINT ticket_priority_check
       CHECK (priority BETWEEN 1 AND 4) NOT VALID;
   ```

   Zweiter Block in A:

   ```sql
   BEGIN;
   ALTER TABLE tickets.ticket VALIDATE CONSTRAINT ticket_priority_check;
   ```

   Die Transaktion bleibt offen. In B, während A noch offen ist, zuerst die
   Sperren von A:

   ```sql
   SELECT a.application_name, l.mode, l.granted
   FROM pg_locks l
   JOIN pg_stat_activity a ON a.pid = l.pid
   WHERE a.application_name = 'exercise_a' AND l.relation = 'tickets.ticket'::regclass
   ORDER BY l.mode;
   ```

   Referenzlauf:

   ```text
    application_name |           mode           | granted 
   ------------------+--------------------------+---------
    exercise_a       | ShareUpdateExclusiveLock | t
   (1 row)
   ```

   Danach, als eigene Ausführung in B:

   ```sql
   UPDATE tickets.ticket SET priority = priority WHERE id = 1;
   ```

   Referenzlauf:

   ```text
   UPDATE 1
   ```

   Schließe danach A ab: `COMMIT;`

   `ADD CONSTRAINT ... NOT VALID` nimmt kurz `ACCESS EXCLUSIVE`. Den
   Bestand prüft es nicht, und die Sperre gibt es mit dem Ende der
   Anweisung sofort wieder frei. Neue und geänderte Zeilen unterliegen der
   Regel ab diesem Zeitpunkt. `VALIDATE CONSTRAINT` liest danach den
   vorhandenen Bestand in einem eigenen Schritt und hält dabei nur
   `SHARE UPDATE EXCLUSIVE`. Das ist deutlich schwächer als die
   `ACCESS EXCLUSIVE`-Sperre der Anweisung davor und lässt gleichzeitiges
   Lesen und Schreiben zu. Das `UPDATE` in B läuft also durch, während A
   seine Transaktion noch hält. Blockiert wird nur eine Sitzung, die
   ihrerseits DDL auf derselben Tabelle ausführen oder sie mit `VACUUM`
   oder `ANALYZE` bearbeiten möchte, etwa eine zweite
   `VALIDATE CONSTRAINT`, `CREATE INDEX CONCURRENTLY`, ein `ANALYZE` oder
   ein `ALTER TABLE ... ADD COLUMN`. Liefe
   `ADD CONSTRAINT` in derselben, noch offenen Transaktion wie
   `VALIDATE CONSTRAINT`, bliebe seine `ACCESS EXCLUSIVE`-Sperre bis zum
   `COMMIT` bestehen und würde auch das `UPDATE` in B blockieren.

2. Finde mit `pg_constraint` und `pg_index` alle Fremdschlüssel im Schema
   `tickets`, deren erste Spalte keinen Index anführt:

   ```sql
   SELECT c.conrelid::regclass AS table_name, c.conname
   FROM pg_constraint c
   WHERE c.contype = 'f'
     AND c.connamespace = 'tickets'::regnamespace
     AND NOT EXISTS (
         SELECT 1 FROM pg_index i
         WHERE i.indrelid = c.conrelid
           AND i.indkey[0] = c.conkey[1]
           AND i.indpred IS NULL
     )
   ORDER BY c.conname;
   ```

   Referenzlauf:

   ```text
      table_name    |        conname         
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

3. Lege `tickets.on_call(agent_id, time_range)` an. Kein Agent darf zwei
   sich überlappende Bereitschaftszeiten haben:

   ```sql
   CREATE EXTENSION IF NOT EXISTS btree_gist;

   CREATE TABLE tickets.on_call (
       agent_id bigint REFERENCES tickets.agent(id),
       time_range tstzrange NOT NULL,
       EXCLUDE USING gist (agent_id WITH =, time_range WITH &&)
   );
   ```

   `EXCLUDE USING gist` verbietet zwei Zeilen, für die alle genannten
   Operatoren gleichzeitig `TRUE` ergeben: gleicher `agent_id`-Wert
   (`=`) und überlappender Zeitraum (`&&`). `btree_gist` liefert die
   GiST-Operatorklasse für `=` auf `bigint`. `tstzrange` bringt seine
   GiST-Unterstützung selbst mit.

4. Füge zwei angrenzende Bereitschaftszeiten für Agent 1 ein:

   ```sql
   INSERT INTO tickets.on_call (agent_id, time_range) VALUES
       (1, tstzrange('2026-09-01 00:00+00', '2026-09-08 00:00+00', '[)')),
       (1, tstzrange('2026-09-08 00:00+00', '2026-09-15 00:00+00', '[)'));
   ```

   Referenzlauf:

   ```text
   INSERT 0 2
   ```

   Die beiden Zeiten teilen keinen gemeinsamen Zeitpunkt: Das Intervall
   `[)` schließt die obere Grenze aus, `2026-09-08 00:00+00` gehört nur
   zum zweiten Zeitraum. Deshalb läuft das `INSERT` durch.

   Füge danach eine Zeit ein, die beide überlappt. Markiere diese
   Anweisung allein und führe sie getrennt vom ersten Block aus. pgAdmin
   schickt eine Markierung mit mehreren Anweisungen als eine Transaktion.
   Stünden beide `INSERT`-Anweisungen darin, rollte der Fehler auch die
   beiden erlaubten Zeilen zurück, und `tickets.on_call` bliebe leer.

   ```sql
   INSERT INTO tickets.on_call (agent_id, time_range)
   VALUES (1, tstzrange('2026-09-05 00:00+00', '2026-09-10 00:00+00', '[)'));
   ```

   Referenzlauf:

   ```text
   ERROR:  23P01: conflicting key value violates exclusion constraint "on_call_agent_id_time_range_excl"
   ```

   Die dritte Zeile überlappt beide vorhandenen Zeiträume und scheitert
   mit SQLSTATE `23P01`. Die Zeile `DETAIL` darunter nennt den neuen
   Schlüssel und den ersten vorhandenen Zeitraum, mit dem er kollidiert.

5. Lege eine Tabelle mit einem aufschiebbaren Fremdschlüssel an und
   beobachte, wann PostgreSQL ihn prüft:

   ```sql
   CREATE TABLE tickets.ticket_ref (
       id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
       ticket_id bigint NOT NULL
           REFERENCES tickets.ticket(id) DEFERRABLE INITIALLY DEFERRED
   );
   ```

   Führe die Transaktion danach als eigenen Block aus. In derselben
   Markierung gehörte `CREATE TABLE` zur Transaktion, und der Fehler beim
   `COMMIT` nähme auch die Tabelle wieder mit:

   ```sql
   BEGIN;
   INSERT INTO tickets.ticket_ref (ticket_id) VALUES (9999999);
   -- INSERT 0 1, ohne Fehler: Ticket 9999999 existiert nicht.
   COMMIT;
   -- ERROR: 23503: insert or update on table "ticket_ref" violates
   -- foreign key constraint "ticket_ref_ticket_id_fkey"
   ```

   Mit `DEFERRABLE INITIALLY DEFERRED` prüft PostgreSQL erst am Ende der
   Transaktion. Das `INSERT` selbst meldet keinen Fehler, obwohl die
   referenzierte Zeile fehlt. Erst `COMMIT` wertet den Fremdschlüssel aus
   und bricht die gesamte Transaktion mit SQLSTATE `23503` ab. Die
   eingefügte Zeile ist danach wieder weg.

## Ergebnis prüfen

```sql
SET TimeZone = 'UTC';
SELECT conname, contype, convalidated FROM pg_constraint
WHERE conrelid IN ('tickets.ticket'::regclass, 'tickets.on_call'::regclass)
  AND contype IN ('c', 'x') ORDER BY conname;
SELECT agent_id, time_range FROM tickets.on_call ORDER BY agent_id, lower(time_range);
RESET TimeZone;
```

Referenzlauf:

```text
             conname              | contype | convalidated 
----------------------------------+---------+--------------
 on_call_agent_id_time_range_excl | x       | t
 ticket_priority_check            | c       | t
(2 rows)

 agent_id |                     time_range                      
----------+-----------------------------------------------------
        1 | ["2026-09-01 00:00:00+00","2026-09-08 00:00:00+00")
        1 | ["2026-09-08 00:00:00+00","2026-09-15 00:00:00+00")
(2 rows)
```

`convalidated = t` gilt für beide Regeln. `ticket_priority_check` hat
Aufgabe 1 ausdrücklich validiert. `on_call_..._excl` entstand mit
`CREATE TABLE`, und PostgreSQL prüft einen dort angelegten Constraint
sofort. Den Zwischenzustand `NOT VALID` erreicht ein Constraint nur
nachträglich per `ALTER TABLE ... ADD CONSTRAINT`. Eine Ausnahme gibt es
seit PostgreSQL 18: Ein `CHECK` oder Fremdschlüssel mit `NOT ENFORCED`
steht auch nach `CREATE TABLE` auf `convalidated = f`, weil PostgreSQL ihn
gar nicht prüft.

## Hinweise

`pg_constraint.conkey` und `pg_index.indkey` speichern Spaltenpositionen.
`conkey` ist ein bei 1 beginnendes Array, `indkey` ein bei 0 beginnendes
`int2vector`. Deshalb vergleicht die Abfrage in Aufgabe 2 `conkey[1]` mit
`indkey[0]`. Ein zusammengesetzter Fremdschlüssel bräuchte einen Vergleich
über alle Positionen. Hier reicht die erste Spalte, weil jeder betroffene
Fremdschlüssel nur eine Spalte umfasst.

`i.indpred IS NULL` lässt Teilindizes aus. `ticket_open_idx` aus Übung 2
beginnt mit `agent_id`, enthält aber nur Tickets, die nicht `closed` sind.
Für die Prüfung des Fremdschlüssels bei einem `DELETE` auf `agent` hilft
er deshalb nicht.

Für den Primärschlüssel legt PostgreSQL automatisch einen Index an. Für
eine Fremdschlüsselspalte muss ihn jemand selbst anlegen. Ohne diesen
Index braucht ein `DELETE` auf der Elterntabelle für die
Fremdschlüsselprüfung einen sequenziellen Scan der Kindtabelle. Rekursive
Abfragen wie in Übung 5 lesen ohne Index bei jedem Rekursionsschritt die
gesamte Tabelle.

`EXCLUDE USING gist` erweitert die Idee eines Unique-Constraints von
Gleichheit auf beliebige Operatoren. Ein Unique-Constraint verbietet zwei
Zeilen mit gleichem Wert. Der Exclusion Constraint hier verbietet zwei
Zeilen, für die Gleichheit auf `agent_id` und Überlappung auf `time_range`
gleichzeitig zutreffen.

`loesung.sql` entfernt zu Beginn `tickets.ticket_ref`,
`tickets.on_call`, `ticket_priority_check` und `comment_parent_idx`
und lässt sich deshalb mehrfach ausführen.
