# SQL-Übung 1: Architektur

## Ziel

Du findest eigene und fremde Sitzungen in `pg_stat_activity`, vergleichst
Zeilenversionen vor und nach einem `UPDATE` und beobachtest Sichtbarkeit über
zwei Verbindungen. Zum Schluss misst du tote Tupel nach einer Massenänderung
und die WAL-Menge eines einzelnen `UPDATE`.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus. Aufgabe 3 braucht
eine zweite Verbindung.

Lege zu Beginn `tickets.messung` an. Sie hält die Werte aus den Aufgaben 2,
4 und 5 fest:

```sql
DROP TABLE IF EXISTS tickets.messung;
CREATE TABLE tickets.messung (
    schritt text PRIMARY KEY,
    wert text
);
```

## Aufgaben

1. Öffne ein zweites Query Tool und setze dort einen eigenen Namen, etwa
   `SET application_name = 'uebung-b';`. Suche beide Sitzungen in
   `pg_stat_activity`:

   ```sql
   SELECT application_name, backend_type, state
   FROM pg_stat_activity
   WHERE datname = current_database()
   ORDER BY application_name;
   ```

   Mit einer laufenden Abfrage in der einen und einer geöffneten, aber
   untätigen zweiten Verbindung:

   ```text
    application_name |  backend_type  | state  
   ------------------+----------------+--------
    uebung-a         | client backend | active
    uebung-b         | client backend | idle
   (2 rows)
   ```

2. Lies `xmin` und `ctid` von Ticket 1, aktualisiere die Zeile und lies
   beide Werte erneut. Führe die drei Anweisungen nacheinander einzeln aus
   (Cursor in die Anweisung, `Execute query`), sonst zeigt `Data Output`
   nur das Ergebnis der letzten Anweisung:

   ```sql
   SELECT xmin, ctid FROM tickets.ticket WHERE id = 1;
   UPDATE tickets.ticket SET priority = priority WHERE id = 1;
   SELECT xmin, ctid FROM tickets.ticket WHERE id = 1;
   ```

   Referenzlauf:

   ```text
    xmin | ctid  
   ------+-------
    1634 | (0,1)
   (1 row)

    xmin |   ctid    
   ------+-----------
    1635 | (19292,8)
   (1 row)
   ```

   Halte fest, ob sich `ctid` geändert hat:

   ```sql
   INSERT INTO tickets.messung (schritt, wert)
   VALUES ('ctid_geaendert', 'true');
   ```

3. Öffne eine zweite Verbindung B neben deiner Verbindung A. In A:

   ```sql
   BEGIN;
   UPDATE tickets.ticket SET subject = subject || ' (A)' WHERE id = 1;
   ```

   Die Transaktion bleibt offen. In B, während A offen ist:

   ```sql
   SELECT id, subject FROM tickets.ticket WHERE id = 1;
   ```

   Referenzlauf, alter Wert:

   ```text
    id |                subject                 
   ----+----------------------------------------
     1 | Ticket #1: Fehlermeldung beim Checkout
   (1 row)
   ```

   In A: `COMMIT;`. Danach in B dieselbe Abfrage erneut ausführen:

   ```text
    id |                  subject                   
   ----+--------------------------------------------
     1 | Ticket #1: Fehlermeldung beim Checkout (A)
   (1 row)
   ```

   B sieht den neuen Wert erst mit einer neuen Abfrage nach dem `COMMIT`
   von A, nicht rückwirkend in der bereits gelesenen Zeile.

4. Schalte `autovacuum` für `ticket` ab, damit kein automatischer Lauf die
   Messung verfälscht:

   ```sql
   ALTER TABLE tickets.ticket SET (autovacuum_enabled = false);
   ```

   Die folgenden vier Blöcke führst du jeweils für sich aus: Block
   markieren, F5, erst danach den nächsten Block. Aktualisiere 10000
   Tickets:

   ```sql
   UPDATE tickets.ticket SET priority = priority WHERE id <= 10000;
   SELECT pg_stat_force_next_flush();
   ```

   `pg_stat_force_next_flush()` sorgt dafür, dass die Sitzung ihre
   Zählerstände direkt nach dem Ende der Transaktion veröffentlicht. Lies
   `n_dead_tup` deshalb erst in der nächsten Ausführung und halte den Wert
   fest:

   ```sql
   INSERT INTO tickets.messung (schritt, wert)
   SELECT 'tote_tupel_nach_update', n_dead_tup::text
   FROM pg_stat_user_tables WHERE schemaname = 'tickets' AND relname = 'ticket';
   SELECT n_dead_tup FROM pg_stat_user_tables
   WHERE schemaname = 'tickets' AND relname = 'ticket';
   ```

   Räume auf. `VACUUM` steht allein in der Markierung:

   ```sql
   VACUUM tickets.ticket;
   ```

   Lies erneut und halte den Wert fest:

   ```sql
   INSERT INTO tickets.messung (schritt, wert)
   SELECT 'tote_tupel_nach_vacuum', n_dead_tup::text
   FROM pg_stat_user_tables WHERE schemaname = 'tickets' AND relname = 'ticket';
   SELECT n_dead_tup FROM pg_stat_user_tables
   WHERE schemaname = 'tickets' AND relname = 'ticket';
   ```

   Setze `autovacuum` danach zurück:

   ```sql
   ALTER TABLE tickets.ticket RESET (autovacuum_enabled);
   ```

5. Merke dir die aktuelle WAL-Position, aktualisiere Ticket 1 erneut und
   bilde die Differenz. Führe die drei Anweisungen einzeln aus.
   `pg_current_wal_lsn()` liefert die geschriebene WAL-Position; das
   `UPDATE` ist darin erst nach seinem `COMMIT` enthalten:

   ```sql
   SELECT pg_current_wal_lsn() AS start_lsn;
   -- start_lsn hier notieren, zum Beispiel in einer temporären Tabelle
   UPDATE tickets.ticket SET priority = priority WHERE id = 1;
   SELECT pg_wal_lsn_diff(pg_current_wal_lsn(), '<start_lsn>');
   ```

   Halte das Ergebnis fest:

   ```sql
   INSERT INTO tickets.messung (schritt, wert)
   VALUES ('wal_bytes', '<gemessene Bytes>');
   ```

## Ergebnis prüfen

```sql
SELECT schritt, wert FROM tickets.messung ORDER BY schritt;
```

Referenzlauf:

```text
        schritt         | wert  
------------------------+-------
 ctid_geaendert         | true
 tote_tupel_nach_update | 10002
 tote_tupel_nach_vacuum | 0
 wal_bytes              | 120
(4 rows)
```

`ctid_geaendert` muss `true` sein, `tote_tupel_nach_update` größer als 0,
`tote_tupel_nach_vacuum` gleich 0 und `wal_bytes` größer als 0. Die genaue
Zahl bei `tote_tupel_nach_update` und `wal_bytes` hängt vom Ausgangszustand
des WAL und von zuvor gelaufenen Transaktionen ab und weicht von Lauf zu
Lauf ab.

## Hinweise

`n_dead_tup` in `pg_stat_user_tables` stammt aus Zählern, die eine Sitzung
erst nach dem Ende ihrer Transaktion veröffentlicht. `pg_stat_force_next_flush()`
erzwingt diese Veröffentlichung beim nächsten Transaktionsende, ohne auf die
nächste automatische Gelegenheit zu warten. Stehen `UPDATE` und Abfrage in
derselben Markierung, laufen sie als eine Transaktion, und die Abfrage liest
noch den alten Zählerstand.

Autovacuum kann `n_dead_tup` schon vor deinem eigenen `VACUUM` senken, wenn
in der Zwischenzeit ein automatischer Lauf startet. Deshalb schaltet die
Übung `autovacuum` für `ticket` während der Messung ab und setzt es danach
zurück. In einer produktiven Datenbank bleibt Autovacuum eingeschaltet;
das Abschalten dient hier ausschließlich der reproduzierbaren Messung.

`VACUUM` läuft nur außerhalb eines Transaktionsblocks. pgAdmin schickt eine
Markierung mit mehreren Anweisungen als eine Transaktion; `VACUUM` darin
endet mit `ERROR: VACUUM cannot run inside a transaction block` (SQLSTATE
`25001`). Allein markiert und bei eingeschaltetem `Auto commit` läuft es.

`xmin` und `ctid` ändern sich bei praktisch jedem `UPDATE`: PostgreSQL legt
immer eine neue Zeilenversion an, auch wenn sich der gespeicherte Wert nicht
ändert. Das gilt unabhängig davon, ob ein HOT-Update greift.

`loesung.sql` läuft abschnittsweise: Jeder Block ab `-- Abschnitt` wird
einzeln markiert und ausgeführt. Aufgabe 3 steht als Kommentar mit den
Blöcken `Verbindung A` und `Verbindung B`, weil eine einzelne
Skriptausführung keine zweite Verbindung hat. Abschnitt 1 entfernt
`tickets.messung`; die Datei lässt sich deshalb mehrfach ausführen, und die
Werte in `tickets.messung` können sich dabei von Lauf zu Lauf
unterscheiden.
