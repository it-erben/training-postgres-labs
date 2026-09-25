# SQL-Übung 15: Backup und Recovery

## Ziel

Die Übung arbeitet mit einem kleinen Ausschnitt des Ticketsystems im
eigenen Schema `restore_uebung`: Agents und Tickets mit Fremdschlüssel und
Identity-Spalten. Du sicherst das Schema logisch im Custom-Format, löschst
danach ein Ticket und spielst die Sicherung neben dem beschädigten Stand
wieder ein. Zum Schluss vergleichst du beide Stände und prüfst, wo die
Sequenz der Tickets steht.

## Ausgangsstand

Die Übung braucht das Schema `tickets` nicht und ändert es nicht. Arbeite
im pgAdmin mit dem RW-Server deiner Servergruppe, Datenbank `app`. Im
Query Tool `Auto commit` an und `Auto rollback on error` aus.

Diesen Block führst du in Aufgabe 1 aus. Er entfernt zuerst die Schemas
aus einem früheren Lauf und legt das Ausgangsschema neu an:

```sql
DROP SCHEMA IF EXISTS restore_uebung, restore_uebung_defekt CASCADE;
CREATE SCHEMA restore_uebung;
CREATE TABLE restore_uebung.agent (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL
);
CREATE TABLE restore_uebung.ticket (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    agent_id bigint NOT NULL REFERENCES restore_uebung.agent,
    subject text NOT NULL
);
INSERT INTO restore_uebung.agent (name) VALUES ('Ada'), ('Linus');
INSERT INTO restore_uebung.ticket (agent_id, subject)
VALUES (1, 'Login'), (2, 'Bericht');
```

Die Sicherung und der Restore laufen über Dialoge des pgAdmin. pgAdmin
ruft dafür `pg_dump` und `pg_restore` in seinem eigenen Container auf; im
code-server brauchst du keine Client-Programme. Die Menünamen stammen aus
der Dokumentation von pgAdmin 4 und können in der Kursumgebung leicht
abweichen.

## Aufgaben

1. Führe den Block aus dem Ausgangsstand im Query Tool aus. Prüfe, dass
   `restore_uebung.ticket` zwei Zeilen enthält.

2. Sichere das Schema im pgAdmin:

   - Öffne im Objektbaum `Databases`, `app`, `Schemas`. Klicke mit der
     rechten Maustaste auf `restore_uebung` und wähle `Backup...`.
   - Reiter `General`: Dateiname `restore_uebung.dump`, Format `Custom`.
     Die übrigen Felder bleiben auf ihren Standardwerten.
   - Starte mit `Backup`. pgAdmin meldet den Abschluss und zeigt den
     Auftrag im Reiter `Processes`.

   Öffne im Reiter `Processes` die Details des Auftrags und notiere den
   `pg_dump`-Aufruf, den pgAdmin ausgeführt hat.

3. Lösche nach der Sicherung ein Ticket:

   ```sql
   DELETE FROM restore_uebung.ticket WHERE id = 2;
   ```

4. Benenne das beschädigte Schema um und spiele die Sicherung ein:

   ```sql
   ALTER SCHEMA restore_uebung RENAME TO restore_uebung_defekt;
   ```

   - Aktualisiere im Objektbaum den Knoten `Schemas` (rechte Maustaste,
     `Refresh...`).
   - Klicke mit der rechten Maustaste auf die Datenbank `app` und wähle
     `Restore...`.
   - Reiter `General`: Format `Custom or tar`, Dateiname
     `restore_uebung.dump`. Starte mit `Restore`.

   Die Sicherung enthält das Schema `restore_uebung` samt Tabellen, Daten,
   Sequenzständen und Constraints. Weil das Schema jetzt
   `restore_uebung_defekt` heißt, entsteht `restore_uebung` neu, und der
   beschädigte Stand bleibt zum Vergleich erhalten.

5. Vergleiche beide Schemas:

   - Zeige die Tickets beider Schemas mit dem Namen des Agents. Welches
     Ticket fehlt im beschädigten Stand?
   - Lies den Stand der Sequenz beider Schemas:

     ```sql
     SELECT schemaname, sequencename, last_value
     FROM pg_sequences
     WHERE schemaname IN ('restore_uebung', 'restore_uebung_defekt')
     ORDER BY schemaname, sequencename;
     ```

     Welche `id` bekommt das nächste Ticket in `restore_uebung`? Warum
     steht die Sequenz im beschädigten Schema auf demselben Wert, obwohl
     dort nur noch ein Ticket steht?

## Ergebnis prüfen

```sql
SELECT (SELECT count(*) FROM restore_uebung.ticket) AS gesichert,
       (SELECT count(*) FROM restore_uebung_defekt.ticket) AS defekt,
       (SELECT last_value FROM restore_uebung.ticket_id_seq) AS sequenz;
```

Referenzlauf mit psql (PostgreSQL 18.6, lokal), Sicherung und Restore mit
`pg_dump` und `pg_restore` statt der pgAdmin-Dialoge:

```text
 gesichert | defekt | sequenz 
-----------+--------+---------
         2 |      1 |       2
(1 row)
```

Meldet die Abfrage `42P01` für `restore_uebung.ticket`, fehlt der Restore
aus Aufgabe 4. Meldet sie `42P01` für `restore_uebung_defekt.ticket`,
fehlt die Umbenennung.

## Hinweise

Eine logische Sicherung enthält den Stand zu Beginn von `pg_dump`. Zeilen,
die während oder nach der Sicherung geschrieben werden, fehlen nach dem
Restore. Die Sicherung eines einzelnen Schemas enthält keine Rollen und
keine Objekte aus anderen Schemas, von denen es abhängt.

Das Custom-Format lässt sich vor dem Restore lesen. `pg_restore -l` zeigt
das Inhaltsverzeichnis. Referenzlauf, Kopfzeilen gekürzt. Jede Zeile
beginnt mit der Nummer des Eintrags im Archiv, danach folgen die OID des
Systemkatalogs und die OID des Objekts; diese Zahlen weichen bei dir ab:

```text
;     Format: CUSTOM
;     Dumped from database version: 18.6 (Debian 18.6-1.pgdg13+2)
;     Dumped by pg_dump version: 18.6
...
7; 2615 16451 SCHEMA - restore_uebung app
228; 1259 16453 TABLE restore_uebung agent app
227; 1259 16452 SEQUENCE restore_uebung agent_id_seq app
230; 1259 16463 TABLE restore_uebung ticket app
229; 1259 16462 SEQUENCE restore_uebung ticket_id_seq app
3463; 0 16453 TABLE DATA restore_uebung agent app
3465; 0 16463 TABLE DATA restore_uebung ticket app
3472; 0 0 SEQUENCE SET restore_uebung agent_id_seq app
3473; 0 0 SEQUENCE SET restore_uebung ticket_id_seq app
3311; 2606 16461 CONSTRAINT restore_uebung agent agent_pkey app
3313; 2606 16472 CONSTRAINT restore_uebung ticket ticket_pkey app
3314; 2606 16473 FK CONSTRAINT restore_uebung ticket ticket_agent_id_fkey app
```

`SEQUENCE SET` setzt die Sequenz auf den gesicherten Stand. Ein `DELETE`
setzt eine Sequenz nie zurück; deshalb steht sie in beiden Schemas auf 2,
und das nächste Ticket in `restore_uebung` bekommt die `id` 3. Wären nach
der Sicherung neue Tickets entstanden, stünde die Sequenz nach dem Restore
wieder auf dem alten Wert. Die Anwendung vergibt dann Schlüssel ein
zweites Mal, die sie außerhalb der Datenbank vielleicht schon genannt hat.

Existiert das Zielschema beim Restore noch, meldet `pg_restore` für jedes
vorhandene Objekt einen Fehler und macht mit dem nächsten weiter. Im
Referenzlauf lautete der erste Fehler
`schema "restore_uebung" already exists`, die Ausgabe begann und endete
so:

```text
pg_restore: error: could not execute query: ERROR: ...
Command was: CREATE SCHEMA restore_uebung;
...
pg_restore: warning: errors ignored on restore: 10
```

Die Sicherung enthält für das Schema und beide Tabellen ein
`ALTER ... OWNER TO app`. Wer mit einer anderen Rolle einspielt, lässt
diese Anweisungen weg: mit `pg_restore --no-owner` oder im Restore-Dialog
im Reiter `Data Options`, Bereich `Do not save`, Schalter `Owner`.

Die Option `Single transaction` im Reiter `Query Options` des
Restore-Dialogs bricht beim ersten Fehler ab und rollt alles zurück.
`Clean before restore` löscht vorhandene Objekte vor dem Einspielen; der
beschädigte Stand wäre danach nicht mehr zum Vergleich da.

pgAdmin legt die Sicherungsdatei in deinem Speicherbereich auf dem
pgAdmin-Server ab. Über `Tools`, `Storage Manager` kannst du sie
herunterladen. Mit `pg_dump` und `pg_restore` auf einem eigenen Rechner
sehen dieselben Schritte so aus, `<verbindung>` steht für die
Verbindungsangabe deines RW-Servers:

```bash
pg_dump "<verbindung>" -Fc -n restore_uebung -f restore_uebung.dump
pg_restore -l restore_uebung.dump
pg_restore -d "<verbindung>" restore_uebung.dump
```

`loesung.sql` entfernt zu Beginn beide Schemas und lässt sich deshalb
mehrfach ausführen. Die Dialogschritte aus Aufgabe 2 und 4 und die
Abfragen aus Aufgabe 5 stehen dort als Kommentar, weil sie den Restore
voraussetzen.
