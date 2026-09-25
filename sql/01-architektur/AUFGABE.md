# SQL-Übung 1: Architektur

## Ziel

Du beobachtest Sitzungen in `pg_stat_activity`, Zeilenversionen bei einem
`UPDATE` und die Sichtbarkeit einer offenen Änderung in einer zweiten
Verbindung. Danach misst du das WAL eines einzelnen `UPDATE` sowie tote
Tupel und Tabellengröße vor und nach `VACUUM`. Vor jedem Schritt legst du
dich auf eine Vorhersage fest. Die Messwerte liefert die Datenbank selbst,
die [Auflösung](#auflösung) am Ende erklärt sie.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Du brauchst zwei
Query Tools A und B, beide mit `Auto commit` an und `Auto rollback on error`
aus. Jeder Codeblock ist eine Ausführung, wie in Übung 0 beschrieben.

Notiere deine Vorhersage, bevor du einen Block ausführst. Die Werte der
Auflösung stammen aus dem ersten Durchlauf nach einem frischen `setup.sql`.

## Aufgaben

### 1. Sitzungen in `pg_stat_activity`

1. In A:

   ```sql
   SET application_name = 'exercise-a';
   ```

2. Öffne das zweite Query Tool B und führe dort aus:

   ```sql
   SET application_name = 'exercise-b';
   ```

3. Vorhersage: Welchen `state` hat die Zeile von A, welchen die von B? Wie
   viele Zeilen liefert die Abfrage insgesamt? In A:

   ```sql
   SELECT application_name, pid = pg_backend_pid() AS own_session,
          backend_type, state
   FROM pg_stat_activity
   WHERE datname = current_database()
   ORDER BY application_name;
   ```

### 2. Zeilenversionen eines `UPDATE`

`ctid` ist die physische Adresse einer Zeilenversion: Seite und Position auf
der Seite. `xmin` nennt die Transaktion, die die Version angelegt hat, `xmax`
die Transaktion, die sie gelöscht oder ersetzt hat. `RETURNING` liefert seit
PostgreSQL 18 mit `old.` und `new.` beide Versionen in einer Zeile.
`pg_stat_get_xact_tuples_hot_updated` zählt die HOT-Updates der laufenden
Transaktion.

1. Vorhersage: Das `UPDATE` setzt `priority` auf den Wert, den die Spalte
   schon hat. Ändert sich `ctid`? Liegt die neue Version auf derselben Seite
   wie die alte? Welche Zahl steht in `old_xmax`? In A:

   ```sql
   UPDATE tickets.ticket SET priority = priority WHERE id = 1
   RETURNING old.ctid AS old_ctid, new.ctid AS new_ctid,
             old.xmin AS old_xmin, old.xmax AS old_xmax,
             new.xmin AS new_xmin,
             pg_stat_get_xact_tuples_hot_updated('tickets.ticket'::regclass)
                 AS hot_updates;
   ```

2. Vorhersage: Du führst denselben Block ein zweites Mal aus. Auf welcher
   Seite landet die neue Version jetzt? Zeigt `hot_updates` eine andere Zahl
   als beim ersten Mal?

### 3. Sichtbarkeit über zwei Verbindungen

1. In A. Beide Anweisungen laufen als eine Ausführung, die Transaktion
   bleibt danach offen:

   ```sql
   BEGIN;
   UPDATE tickets.ticket SET subject = subject || ' (A)' WHERE id = 1;
   ```

2. Vorhersage: Wartet die Abfrage in B, bis A die Transaktion beendet?
   Welchen Betreff und welche `ctid` sieht B, und was steht in `xmax`? In B:

   ```sql
   SELECT ctid, xmin, xmax, subject FROM tickets.ticket WHERE id = 1;
   ```

3. Vorhersage: Was liefert dieselbe Abfrage in A? In A:

   ```sql
   SELECT ctid, xmin, xmax, subject FROM tickets.ticket WHERE id = 1;
   ```

4. In A:

   ```sql
   COMMIT;
   ```

5. Vorhersage: Was ändert sich, wenn B die Abfrage jetzt erneut ausführt?
   In B:

   ```sql
   SELECT ctid, xmin, xmax, subject FROM tickets.ticket WHERE id = 1;
   ```

### 4. WAL eines einzelnen `UPDATE`

`pg_current_wal_lsn()` nennt die Position, bis zu der PostgreSQL das WAL
geschrieben hat. `set_config` legt die Startposition als Einstellung der
Sitzung ab, `current_setting` liest sie wieder. Die Differenz rechnet die
Datenbank dann selbst aus. Alle Schritte laufen in A.

1. Startposition merken:

   ```sql
   SELECT set_config('exercise.wal_start', pg_current_wal_lsn()::text, false);
   ```

2. Vorhersage: Wie viel WAL erzeugt das `UPDATE` einer einzigen Zeile, deren
   Wert gleich bleibt: einige hundert Byte, einige Kilobyte oder mehr als
   100 kB?

   ```sql
   UPDATE tickets.ticket SET priority = priority WHERE id = 1;
   ```

3. Differenz zur Startposition:

   ```sql
   SELECT pg_wal_lsn_diff(pg_current_wal_lsn(),
                          current_setting('exercise.wal_start')::pg_lsn)
              AS wal_bytes,
          (SELECT checkpoint_time FROM pg_control_checkpoint())
              AS last_checkpoint;
   ```

   Mit `Auto commit` endet das `UPDATE` aus Schritt 2 mit seinem `COMMIT`.
   Das WAL des `UPDATE` kann schon vor dem `COMMIT` geschrieben sein. Der
   `COMMIT` schreibt das WAL spätestens bis einschließlich seines
   Commit-Eintrags. Nach dem `COMMIT` ist das `UPDATE` deshalb sicher in der
   Differenz enthalten.

4. Vorhersage: Du wiederholst die Schritte 1 bis 3 sofort. Wird `wal_bytes`
   größer, kleiner oder bleibt es gleich?

### 5. Tote Tupel und Tabellengröße

Die Schritte laufen in A. `VACUUM` in Schritt 5 und 9 steht allein in seinem
Block.

1. Schalte Autovacuum für `ticket` ab, damit kein automatischer Lauf die
   Messung verändert:

   ```sql
   ALTER TABLE tickets.ticket SET (autovacuum_enabled = false);
   ```

2. Tabellengröße merken:

   ```sql
   SELECT set_config('exercise.size_start',
                     pg_relation_size('tickets.ticket')::text, false);
   ```

3. Vorhersage: Wie viele tote Tupel zählt PostgreSQL nach dem `UPDATE` von
   20000 Zeilen? Wächst die Tabelle, und wenn ja, um wie viele Seiten zu
   8 kB? Beide Anweisungen laufen als eine Ausführung:

   ```sql
   UPDATE tickets.ticket SET priority = priority WHERE id <= 20000;
   SELECT pg_stat_force_next_flush();
   ```

   `pg_stat_force_next_flush()` lässt die Sitzung ihre Zähler am Ende dieser
   Ausführung sofort veröffentlichen. Die Messung in Schritt 4 liest sie.

4. Messen:

   ```sql
   SELECT n_dead_tup,
          (pg_relation_size('tickets.ticket')
           - current_setting('exercise.size_start')::bigint) / 8192
              AS new_pages
   FROM pg_stat_user_tables
   WHERE relid = 'tickets.ticket'::regclass;
   ```

5. Vorhersage: Was ändert `VACUUM` an `n_dead_tup`, was an `new_pages`?

   ```sql
   VACUUM tickets.ticket;
   ```

6. Führe die Messung aus Schritt 4 erneut aus.

7. Vorhersage: Du aktualisierst dieselben 20000 Zeilen noch einmal. Um wie
   viele Seiten wächst die Tabelle diesmal? Wieder eine Ausführung:

   ```sql
   UPDATE tickets.ticket SET priority = priority WHERE id <= 20000;
   SELECT pg_stat_force_next_flush();
   ```

8. Führe die Messung aus Schritt 4 erneut aus.

9. Räume die toten Tupel aus Schritt 7 weg:

   ```sql
   VACUUM tickets.ticket;
   ```

10. Schalte Autovacuum wieder ein:

    ```sql
    ALTER TABLE tickets.ticket RESET (autovacuum_enabled);
    ```

## Ergebnis prüfen

Die Abfrage liest den Zustand nach Aufgabe 5 und liefert je Aussage `true`
oder `false`:

```sql
SELECT
    (SELECT subject LIKE '% (A)'
     FROM tickets.ticket WHERE id = 1) AS subject_from_a,
    (SELECT n_dead_tup = 0
     FROM pg_stat_user_tables
     WHERE relid = 'tickets.ticket'::regclass) AS no_dead_tuples,
    (SELECT NOT coalesce('autovacuum_enabled=false' = ANY (reloptions), false)
     FROM pg_class
     WHERE oid = 'tickets.ticket'::regclass) AS autovacuum_on;
```

```text
 subject_from_a | no_dead_tuples | autovacuum_on
----------------+----------------+---------------
 t              | t              | t
(1 row)
```

Stelle zum Schluss den Betreff von Ticket 1 wieder her. Keine andere Übung
liest ihn, aber so beginnt ein weiterer Durchlauf mit demselben Text:

```sql
UPDATE tickets.ticket
SET subject = 'Ticket #1: Fehlermeldung beim Checkout'
WHERE id = 1;
```

## Hinweise

`VACUUM` läuft nur außerhalb eines Transaktionsblocks. Steht es mit einer
weiteren Anweisung in einer Ausführung, endet es mit
`ERROR: VACUUM cannot run inside a transaction block` (SQLSTATE `25001`).

`n_dead_tup` stammt aus Zählern, die eine Sitzung erst nach dem Ende ihrer
Transaktion veröffentlicht. Ohne `pg_stat_force_next_flush()` tut sie das
höchstens einmal je Sekunde, in einem lokalen Test zum Teil erst nach
mehreren Sekunden. Steht die Messung in derselben Ausführung wie das `UPDATE`, liest
sie noch den alten Stand.

Autovacuum ist nur während Aufgabe 5 abgeschaltet, damit die Zahlen
reproduzierbar bleiben. In einer produktiven Datenbank bleibt es
eingeschaltet.

`exercise.wal_start` und `exercise.size_start` gelten bis zum Ende der
Sitzung. Verbindet sich das Query Tool neu, führe den Schritt, der den Wert
merkt, noch einmal aus. Ohne ihn endet `current_setting` mit
`unrecognized configuration parameter` (SQLSTATE `42704`).

`loesung.sql` läuft abschnittsweise: Jeder Block ab `-- Abschnitt` ist eine
Ausführung. Aufgabe 3 steht darin als Kommentar, weil ein Skript nur eine
Verbindung hat. Ausführbar bleibt davon das `UPDATE` von A, damit die
Prüfabfrage denselben Stand liest.

## Auflösung

Die Ausgaben stammen aus dem Kurscluster `trainer-pg`, PostgreSQL 18.6,
Rolle `app`, erster Durchlauf nach `setup.sql`. Transaktionsnummern,
Prozess-IDs und Zeitpunkte weichen in jeder Umgebung ab.

### Aufgabe 1

```text
 application_name | own_session |  backend_type  | state
------------------+-------------+----------------+--------
 exercise-a       | t           | client backend | active
 exercise-b       | f           | client backend | idle
(2 rows)
```

A führt in diesem Moment die Abfrage aus und steht deshalb auf `active`. B
ist verbunden und wartet auf die nächste Anweisung, also `idle`. Jedes Query
Tool hat ein eigenes Backend mit eigener `pid`.

Im pgAdmin liefert die Abfrage mehr als zwei Zeilen. Der Objektbaum erscheint
als `pgAdmin 4 - DB:app`, jedes Query Tool ohne eigenen Namen als
`pgAdmin 4 - CONN:` mit einer Zahl. Ist die Autovervollständigung
eingeschaltet, hält ein Query Tool eine zweite Verbindung.

### Aufgabe 2

Erste Ausführung:

```text
 old_ctid | new_ctid  | old_xmin | old_xmax | new_xmin | hot_updates
----------+-----------+----------+----------+----------+-------------
 (0,1)    | (19292,8) |     2088 |     2097 |     2097 |           0
(1 row)
```

Zweite Ausführung:

```text
 old_ctid  | new_ctid  | old_xmin | old_xmax | new_xmin | hot_updates
-----------+-----------+----------+----------+----------+-------------
 (19292,8) | (19292,9) |     2097 |     2098 |     2098 |           1
(1 row)
```

Jedes `UPDATE` legt eine neue Zeilenversion an, auch wenn sich kein Wert
ändert. `ctid` ändert sich deshalb bei beiden Ausführungen. Die alte Version
trägt danach in `xmax` die Nummer der Transaktion, die das `UPDATE`
ausgeführt hat. Dieselbe Nummer steht in `xmin` der neuen Version.

Ticket 1 liegt nach `setup.sql` auf Seite 0. `setup.sql` füllt die Seiten
vollständig, die neue Version passt dort nicht mehr hin. PostgreSQL legt sie
auf der letzten Seite 19292 ab, die noch Platz hat. Das ist kein HOT-Update,
`hot_updates` bleibt 0.

Bei der zweiten Ausführung liegt die Zeile auf Seite 19292. Dort ist Platz,
die neue Version bleibt auf derselben Seite, und PostgreSQL führt ein
HOT-Update aus. Ein HOT-Update legt keinen neuen Indexeintrag an. Es setzt
zweierlei voraus: Platz auf derselben Seite, und keine indizierte Spalte
ändert ihren Wert. Nach `setup.sql` ist nur `id` indiziert (`ticket_pkey`),
`priority` in keiner Übung.

Bei einem weiteren Durchlauf liegt Ticket 1 schon auf einer Seite mit Platz.
Dann ist bereits die erste Ausführung ein HOT-Update.

### Aufgabe 3

B, während A offen ist:

```text
   ctid    | xmin | xmax |                subject
-----------+------+------+----------------------------------------
 (19292,9) | 2098 | 2099 | Ticket #1: Fehlermeldung beim Checkout
(1 row)
```

A vor dem `COMMIT` und B nach dem `COMMIT`:

```text
    ctid    | xmin | xmax |                  subject
------------+------+------+--------------------------------------------
 (19292,10) | 2099 |    0 | Ticket #1: Fehlermeldung beim Checkout (A)
(1 row)
```

B wartet nicht. Lesende Zugriffe warten nicht auf Zeilensperren. B sieht die
alte Version mit dem alten Betreff. Ihr `xmax` enthält bereits die
Transaktion 2099 von A. Weil A noch nicht bestätigt hat, bleibt die alte
Version für B sichtbar. `xmax` allein entscheidet also nicht über die
Sichtbarkeit, maßgeblich ist der Status der Transaktion in `xmax`.

A sieht die eigene Änderung schon vor dem `COMMIT`. Nach dem `COMMIT`
bekommt die nächste Abfrage von B unter `READ COMMITTED` einen neuen
Snapshot und liest die neue Version.

### Aufgabe 4

Die erste Messung lag im Referenzlauf nach einem Checkpoint:

```text
 wal_bytes |    last_checkpoint
-----------+------------------------
      2440 | 2026-09-25 18:09:35+00
(1 row)
```

Die Wiederholung direkt danach:

```text
 wal_bytes |    last_checkpoint
-----------+------------------------
       288 | 2026-09-25 18:09:35+00
(1 row)
```

Ein `UPDATE` einer Zeile ohne Full Page Image erzeugt einige hundert Byte:
den Eintrag für die neue Zeilenversion und den Commit-Eintrag. Der
Kurscluster läuft mit `wal_level = logical` und schreibt dabei die neue
Version vollständig ins WAL, 288 Byte. Ein lokaler Server mit
`wal_level = replica` schrieb für dasselbe `UPDATE` 112 Byte.

Ändert sich eine Seite zum ersten Mal nach einem Checkpoint, schreibt
PostgreSQL zusätzlich ein Abbild der ganzen Seite ins WAL, das Full Page
Image. Den leeren Bereich der Seite lässt es dabei weg. Seite 19292 ist
großteils leer, deshalb 2440 Byte statt rund 8 kB. `last_checkpoint`
liegt im Referenzlauf nach dem `COMMIT` aus Aufgabe 3, der die Seite zuletzt
geändert hatte. Die zweite Messung braucht kein Full Page Image mehr und
bleibt bei 288 Byte. Ohne Checkpoint dazwischen zeigen beide Messungen
288 Byte.

`wal_bytes` zählt das WAL der ganzen Instanz, auch das anderer Sitzungen und
von Autovacuum. Kurz nach `setup.sql` bereinigt Autovacuum `comment` und
`ticket`; eine Messung in diesem Zeitraum zeigte lokal 231376 Byte. Zeigt
`wal_bytes` mehrere Megabyte, lag ein Wechsel der WAL-Datei zwischen Start
und Messung. Der Kurscluster wechselt spätestens alle fünf Minuten
(`archive_timeout = 300`). Wiederhole in beiden Fällen die Schritte 1 bis 3.

### Aufgabe 5

| Messung        | `n_dead_tup` | `new_pages` |
| -------------- | ------------ | ----------- |
| nach Schritt 3 | 20005        | 479         |
| nach Schritt 5 | 0            | 479         |
| nach Schritt 7 | 20000        | 480         |

Jede der 20000 alten Versionen ist nach dem `UPDATE` tot. Die fünf weiteren
stammen von Ticket 1 aus den Aufgaben 2 bis 4. Die neuen Versionen passen
nicht auf die vollen Seiten ihrer alten Versionen, die Tabelle wächst um 479
Seiten, rund 3,7 MB.

`VACUUM` entfernt die toten Versionen, `n_dead_tup` fällt auf 0. Die Datei
bleibt gleich groß. `VACUUM` trägt den frei gewordenen Platz in die Free
Space Map ein, gibt ihn aber nicht an das Betriebssystem zurück. Nur leere
Seiten am Ende der Datei kann es abschneiden.

Das zweite `UPDATE` legt seine 20000 neuen Versionen in den frei gewordenen
Platz. Die Tabelle wächst nur um eine Seite.

Bei 10000 Zeilen fehlt dieser Effekt: Die toten Versionen liegen dann auf
rund 1,2 % der Seiten. Unter 2 % überspringt `VACUUM` mit der Voreinstellung
`INDEX_CLEANUP AUTO` die Indexbereinigung. Die toten Zeilenzeiger bleiben
dann stehen, und der Platz erscheint nicht in der Free Space Map. In einem
lokalen Test mit 10000 Zeilen wuchs die Tabelle beim ersten `UPDATE` um 239
Seiten und beim zweiten um weitere 238.

In einem weiteren Durchlauf zeigt `new_pages` schon nach Schritt 3 den Wert
0, weil der Platz aus dem ersten Durchlauf frei ist.
