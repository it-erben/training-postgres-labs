# SQL-Übung 1: Architektur

## Ziel

Du beobachtest Sitzungen in `pg_stat_activity`, Zeilenversionen bei einem
`UPDATE` und die Sichtbarkeit einer offenen Änderung in einer zweiten
Verbindung. Danach misst du das WAL eines einzelnen `UPDATE` sowie tote
Tupel und Tabellengröße vor und nach `VACUUM`. Nach jeder Abfrage liest du
einen Wert aus deiner eigenen Ausgabe ab. Ein eingeklappter Block darunter
enthält die Ausgabe eines Referenzlaufs mit einer Erklärung, was die Werte
bedeuten.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Du brauchst zwei
Query Tools A und B, beide mit `Auto commit` an und `Auto rollback on error`
aus. Jeder Codeblock ist eine Ausführung, wie in Übung 0 beschrieben.

Führe jede Abfrage zuerst selbst aus und beantworte die Frage unter
**Ablesen** aus deiner Ausgabe. Klappe erst danach den Block
"Referenzausgabe und Erklärung" auf und vergleiche.

Die Referenzausgaben stammen aus dem Kurscluster `trainer-pg`, PostgreSQL
18.6, Rolle `app`, erster Durchlauf nach einem frischen `setup.sql`.
Transaktionsnummern, Prozess-IDs und Zeitpunkte weichen in jeder Umgebung
ab.

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

3. In A:

   ```sql
   SELECT application_name, pid = pg_backend_pid() AS own_session,
          backend_type, state
   FROM pg_stat_activity
   WHERE datname = current_database()
   ORDER BY application_name;
   ```

   **Ablesen:** Welchen `state` zeigen die Zeilen `exercise-a` und
   `exercise-b`?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    application_name | own_session |  backend_type  | state
   ------------------+-------------+----------------+--------
    exercise-a       | t           | client backend | active
    exercise-b       | f           | client backend | idle
   (2 rows)
   ```

   **Was das Ergebnis zeigt:** Jedes Query Tool hat ein eigenes Backend mit
   eigener `pid`, deshalb ist `own_session` nur in der Zeile von A `t`. A
   führt in diesem Moment die Abfrage aus und steht auf `active`. B ist
   verbunden und wartet auf die nächste Anweisung, also `idle`. Diese beiden
   Zeilen sehen bei dir genauso aus.

   Im pgAdmin liefert die Abfrage mehr als zwei Zeilen. Der Objektbaum
   erscheint als `pgAdmin 4 - DB:app`, jedes Query Tool ohne eigenen Namen
   als `pgAdmin 4 - CONN:` mit einer Zahl. Ist die Autovervollständigung
   eingeschaltet, hält ein Query Tool eine zweite Verbindung.

   </details>

### 2. Zeilenversionen eines `UPDATE`

`ctid` ist die physische Adresse einer Zeilenversion. Sie enthält die Seite
und die Position auf der Seite. `xmin` nennt die Transaktion, die die
Version angelegt hat, `xmax` die Transaktion, die sie gelöscht oder ersetzt
hat. `RETURNING` liefert seit PostgreSQL 18 mit `old.` und `new.` beide
Versionen in einer Zeile. `pg_stat_get_xact_tuples_hot_updated` zählt die
HOT-Updates der laufenden Transaktion.

1. Das `UPDATE` setzt `priority` auf den Wert, den die Spalte schon hat. In
   A:

   ```sql
   UPDATE tickets.ticket SET priority = priority WHERE id = 1
   RETURNING old.ctid AS old_ctid, new.ctid AS new_ctid,
             old.xmin AS old_xmin, old.xmax AS old_xmax,
             new.xmin AS new_xmin,
             pg_stat_get_xact_tuples_hot_updated('tickets.ticket'::regclass)
                 AS hot_updates;
   ```

   **Ablesen:** Welche `ctid` hat die Zeile vor (`old_ctid`) und nach dem
   `UPDATE` (`new_ctid`)?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    old_ctid | new_ctid  | old_xmin | old_xmax | new_xmin | hot_updates
   ----------+-----------+----------+----------+----------+-------------
    (0,1)    | (19292,8) |     2088 |     2097 |     2097 |           0
   (1 row)
   ```

   **Was das Ergebnis zeigt:** Jedes `UPDATE` legt eine neue Zeilenversion
   an, auch wenn sich kein Wert ändert. Die alte Version trägt danach in
   `xmax` die Nummer 2097 der Transaktion, die das `UPDATE` ausgeführt hat.
   Dieselbe Nummer steht in `xmin` der neuen Version. Ticket 1 liegt nach
   `setup.sql` auf Seite 0, und `setup.sql` füllt die Seiten vollständig. Die
   neue Version landet deshalb auf der letzten Seite 19292, die noch Platz
   hat. Das ist kein HOT-Update, `hot_updates` bleibt 0.

   Bei dir weichen die Transaktionsnummern ab, eventuell auch Seite und
   Position in `new_ctid`. Gleich bleibt: `new_ctid` ist eine andere Adresse
   als `old_ctid`, `old_xmax` und `new_xmin` tragen dieselbe Nummer, und
   `hot_updates` ist 0.

   </details>

2. Führe denselben Block ein zweites Mal aus.

   **Ablesen:** Auf welcher Seite liegen `old_ctid` und `new_ctid` jetzt,
   und welchen Wert zeigt `hot_updates`?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    old_ctid  | new_ctid  | old_xmin | old_xmax | new_xmin | hot_updates
   -----------+-----------+----------+----------+----------+-------------
    (19292,8) | (19292,9) |     2097 |     2098 |     2098 |           1
   (1 row)
   ```

   **Was das Ergebnis zeigt:** Die Zeile liegt jetzt auf Seite 19292, und
   dort ist Platz. Die neue Version bleibt auf derselben Seite, PostgreSQL
   führt ein HOT-Update aus und `hot_updates` zeigt 1. Ein HOT-Update legt
   keinen neuen Indexeintrag an. Es braucht Platz auf derselben Seite, und
   keine indizierte Spalte darf ihren Wert ändern. Nach `setup.sql` ist nur
   `id` indiziert (`ticket_pkey`), `priority` in keiner Übung.

   Seitennummer, Position und Transaktionsnummern weichen bei dir ab. Gleich
   bleibt: `old_ctid` ist dein `new_ctid` aus Schritt 1, beide Adressen
   nennen dieselbe Seite, und `hot_updates` ist 1.

   </details>

### 3. Sichtbarkeit über zwei Verbindungen

1. In A. Beide Anweisungen laufen als eine Ausführung, die Transaktion
   bleibt danach offen:

   ```sql
   BEGIN;
   UPDATE tickets.ticket SET subject = subject || ' (A)' WHERE id = 1;
   ```

2. In B:

   ```sql
   SELECT ctid, xmin, xmax, subject FROM tickets.ticket WHERE id = 1;
   ```

   **Ablesen:** Welche Nummer steht in `xmax`, und welchen Betreff sieht B?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
      ctid    | xmin | xmax |                subject
   -----------+------+------+----------------------------------------
    (19292,9) | 2098 | 2099 | Ticket #1: Fehlermeldung beim Checkout
   (1 row)
   ```

   **Was das Ergebnis zeigt:** Die Abfrage kommt sofort zurück, lesende
   Zugriffe warten nicht auf Zeilensperren. B sieht die Version `(19292,9)`
   aus Aufgabe 2 mit dem alten Betreff. Ihr `xmax` enthält bereits die
   Transaktion 2099 von A. Solange A nicht bestätigt hat, bleibt die alte
   Version für B trotzdem sichtbar. Ungültig wird sie erst, wenn die
   Transaktion in `xmax` bestätigt ist.

   `ctid` und die Nummern weichen bei dir ab. Gleich bleibt: `ctid` ist dein
   `new_ctid` aus Aufgabe 2, `xmax` ist ungleich 0, und der Betreff hat
   noch keinen Zusatz `(A)`.

   </details>

3. In A:

   ```sql
   SELECT ctid, xmin, xmax, subject FROM tickets.ticket WHERE id = 1;
   ```

   **Ablesen:** Welches `xmin` zeigt A, verglichen mit dem `xmax` aus
   Schritt 2?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
       ctid    | xmin | xmax |                  subject
   ------------+------+------+--------------------------------------------
    (19292,10) | 2099 |    0 | Ticket #1: Fehlermeldung beim Checkout (A)
   (1 row)
   ```

   **Was das Ergebnis zeigt:** A sieht die eigene Änderung schon vor dem
   `COMMIT`. Die neue Version `(19292,10)` trägt in `xmin` die eigene
   Transaktion 2099. `xmax` ist 0, keine Transaktion hat diese Version
   gelöscht oder ersetzt.

   Bei dir weichen `ctid` und `xmin` ab. Gleich bleibt: `xmin` in A ist die
   Nummer, die B in Schritt 2 als `xmax` gesehen hat, und die `ctid`
   unterscheidet sich von der in B.

   </details>

4. In A:

   ```sql
   COMMIT;
   ```

5. In B:

   ```sql
   SELECT ctid, xmin, xmax, subject FROM tickets.ticket WHERE id = 1;
   ```

   **Ablesen:** Welche `ctid` sieht B jetzt?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
       ctid    | xmin | xmax |                  subject
   ------------+------+------+--------------------------------------------
    (19292,10) | 2099 |    0 | Ticket #1: Fehlermeldung beim Checkout (A)
   (1 row)
   ```

   **Was das Ergebnis zeigt:** B liest jetzt dieselbe Version wie A in
   Schritt 3. Unter `READ COMMITTED` bekommt jede Abfrage einen neuen
   Snapshot, und in diesem ist Transaktion 2099 bestätigt.

   Deine Werte für `ctid` und `xmin` weichen von der Referenz ab und stimmen
   mit deiner Ausgabe von A aus Schritt 3 überein.

   </details>

### 4. WAL eines einzelnen `UPDATE`

`pg_current_wal_lsn()` nennt die Position, bis zu der PostgreSQL das WAL
geschrieben hat. `set_config` legt die Startposition als Einstellung der
Sitzung ab, `current_setting` liest sie wieder. Die Differenz rechnet die
Datenbank dann selbst aus. Alle Schritte laufen in A.

1. Startposition merken:

   ```sql
   SELECT set_config('exercise.wal_start', pg_current_wal_lsn()::text, false);
   ```

2. Eine Zeile aktualisieren, deren Wert gleich bleibt:

   ```sql
   UPDATE tickets.ticket SET priority = priority WHERE id = 1;
   ```

   Mit `Auto commit` endet das `UPDATE` mit seinem `COMMIT`. Der `COMMIT`
   schreibt das WAL spätestens bis einschließlich seines Commit-Eintrags.
   Die Differenz in Schritt 3 enthält das `UPDATE` deshalb sicher.

3. Differenz zur Startposition:

   ```sql
   SELECT pg_wal_lsn_diff(pg_current_wal_lsn(),
                          current_setting('exercise.wal_start')::pg_lsn)
              AS wal_bytes,
          (SELECT checkpoint_time FROM pg_control_checkpoint())
              AS last_checkpoint;
   ```

   **Ablesen:** Wie viele Byte zeigt `wal_bytes`?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   Referenzlauf, nach einem Checkpoint:

   ```text
    wal_bytes |    last_checkpoint
   -----------+------------------------
         2440 | 2026-09-25 18:09:35+00
   (1 row)
   ```

   **Was das Ergebnis zeigt:** Ändert sich eine Seite zum ersten Mal nach
   einem Checkpoint, schreibt PostgreSQL zusätzlich ein Abbild der ganzen
   Seite ins WAL, das Full Page Image. Den leeren Bereich der Seite lässt es
   dabei weg. Seite 19292 ist großteils leer, deshalb 2440 Byte statt rund
   8 kB. `last_checkpoint` liegt nach dem `COMMIT` aus Aufgabe 3, der die
   Seite zuletzt geändert hatte.

   Byte-Zahl und Zeitpunkt weichen bei dir ab. Liegt dein `last_checkpoint`
   nach deinem `COMMIT` aus Aufgabe 3, enthält `wal_bytes` ein Full Page
   Image und liegt im Bereich einiger tausend Byte. Liegt kein Checkpoint
   dazwischen, zeigt schon diese Messung denselben Wert wie Schritt 4.

   </details>

4. Wiederhole die Schritte 1 bis 3 sofort.

   **Ablesen:** Wie viele Byte zeigt `wal_bytes` jetzt, verglichen mit
   Schritt 3?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    wal_bytes |    last_checkpoint
   -----------+------------------------
          288 | 2026-09-25 18:09:35+00
   (1 row)
   ```

   **Was das Ergebnis zeigt:** Seit dem letzten Checkpoint hat die Seite ihr
   Full Page Image schon. Übrig bleiben der Eintrag für die neue
   Zeilenversion und der Commit-Eintrag, zusammen einige hundert Byte. Der
   Kurscluster läuft mit `wal_level = logical` und schreibt die neue Version
   dabei vollständig ins WAL, 288 Byte. Ein lokaler Server mit
   `wal_level = replica` schrieb für dasselbe `UPDATE` 112 Byte.

   Die genaue Byte-Zahl hängt bei dir von `wal_level` ab. Gleich bleibt:
   einige hundert Byte, solange `last_checkpoint` denselben Zeitpunkt wie in
   Schritt 3 zeigt.

   </details>

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

3. 20000 Zeilen aktualisieren. Beide Anweisungen laufen als eine Ausführung:

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

   **Ablesen:** Welche Werte zeigen `n_dead_tup` und `new_pages`?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    n_dead_tup | new_pages
   ------------+-----------
         20005 |       479
   (1 row)
   ```

   **Was das Ergebnis zeigt:** Jede der 20000 alten Versionen ist nach dem
   `UPDATE` tot. Die fünf weiteren stammen von Ticket 1 aus den Aufgaben 2
   bis 4. Die neuen Versionen passen nicht auf die vollen Seiten ihrer alten
   Versionen, die Tabelle wächst um 479 Seiten zu 8 kB, rund 3,7 MB.

   Bei dir liegt `n_dead_tup` knapp über 20000. Der Rest hängt davon ab, wie
   oft du Ticket 1 in Aufgabe 2 bis 4 aktualisiert hast. `new_pages` hängt
   davon ab, wie viel freien Platz die Tabelle vorher hatte. Gleich bleibt:
   rund 20000 tote Tupel und ein Zuwachs von mehreren hundert Seiten.

   </details>

5. Tote Tupel entfernen:

   ```sql
   VACUUM tickets.ticket;
   ```

6. Führe die Messung aus Schritt 4 erneut aus.

   **Ablesen:** Welchen Wert zeigt `n_dead_tup` jetzt, und hat sich
   `new_pages` gegenüber Schritt 4 verändert?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    n_dead_tup | new_pages
   ------------+-----------
             0 |       479
   (1 row)
   ```

   **Was das Ergebnis zeigt:** `VACUUM` entfernt die toten Versionen,
   `n_dead_tup` fällt auf 0. Die Datei bleibt gleich groß. `VACUUM` trägt
   den frei gewordenen Platz in die Free Space Map ein und gibt ihn nicht an
   das Betriebssystem zurück. Nur leere Seiten am Ende der Datei kann es
   abschneiden.

   Bei dir zeigt `new_pages` deinen Wert aus Schritt 4, `n_dead_tup` ist 0.

   </details>

7. Dieselben 20000 Zeilen noch einmal aktualisieren, wieder eine Ausführung:

   ```sql
   UPDATE tickets.ticket SET priority = priority WHERE id <= 20000;
   SELECT pg_stat_force_next_flush();
   ```

8. Führe die Messung aus Schritt 4 erneut aus.

   **Ablesen:** Um wie viele Seiten ist `new_pages` gegenüber Schritt 6
   gewachsen?

   <details>
   <summary>Referenzausgabe und Erklärung</summary>

   ```text
    n_dead_tup | new_pages
   ------------+-----------
         20000 |       480
   (1 row)
   ```

   **Was das Ergebnis zeigt:** Das zweite `UPDATE` legt seine 20000 neuen
   Versionen in den Platz, den `VACUUM` in Schritt 5 frei gemacht hat. Die
   Tabelle wächst nur um eine Seite, von 479 auf 480. `n_dead_tup` zählt
   wieder die 20000 alten Versionen.

   Der Stand von `new_pages` kann bei dir abweichen. Gleich bleibt: ein
   Zuwachs von höchstens wenigen Seiten gegenüber Schritt 6, verglichen mit
   mehreren hundert Seiten in Schritt 4, und `n_dead_tup` zeigt 20000.

   </details>

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

Bei einem weiteren Durchlauf ohne frisches `setup.sql` weichen einige Werte
von den Referenzausgaben ab. Ticket 1 liegt dann schon auf einer Seite mit
Platz, und bereits die erste Ausführung in Aufgabe 2 ist ein HOT-Update. In
Aufgabe 5 zeigt `new_pages` schon in Schritt 4 den Wert 0, weil der Platz
aus dem ersten Durchlauf frei ist.

`wal_bytes` zählt das WAL der ganzen Instanz, auch das anderer Sitzungen und
von Autovacuum. Kurz nach `setup.sql` bereinigt Autovacuum `comment` und
`ticket`; eine Messung in diesem Zeitraum zeigte lokal 231376 Byte. Zeigt
`wal_bytes` mehrere Megabyte, lag ein Wechsel der WAL-Datei zwischen Start
und Messung. Der Kurscluster wechselt spätestens alle fünf Minuten
(`archive_timeout = 300`). Wiederhole in beiden Fällen die Schritte 1 bis 3
aus Aufgabe 4.

Aufgabe 5 aktualisiert 20000 Zeilen, weil bei 10000 Zeilen der Effekt aus
Schritt 8 fehlt. Die toten Versionen liegen dann auf rund 1,2 % der Seiten.
Unter 2 % überspringt `VACUUM` mit der Voreinstellung `INDEX_CLEANUP AUTO`
die Indexbereinigung. Die toten Zeilenzeiger bleiben stehen, und der Platz
erscheint nicht in der Free Space Map. In einem lokalen Test mit 10000
Zeilen wuchs die Tabelle beim ersten `UPDATE` um 239 Seiten und beim zweiten
um weitere 238.

`VACUUM` läuft nur außerhalb eines Transaktionsblocks. Steht es mit einer
weiteren Anweisung in einer Ausführung, endet es mit
`ERROR: VACUUM cannot run inside a transaction block` (SQLSTATE `25001`).

`n_dead_tup` stammt aus Zählern, die eine Sitzung erst nach dem Ende ihrer
Transaktion veröffentlicht. Ohne `pg_stat_force_next_flush()` tut sie das
höchstens einmal je Sekunde, in einem lokalen Test zum Teil erst nach
mehreren Sekunden. Steht die Messung in derselben Ausführung wie das
`UPDATE`, liest sie noch den alten Stand.

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
