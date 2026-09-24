# SQL-Übung 3: Umstieg von Oracle

## Ziel

Du prüfst fünf Verhaltensunterschiede zwischen Oracle und PostgreSQL direkt
an deiner Datenbank: leere Zeichenketten gegenüber NULL, die Verkettung mit
NULL, den Rollback von DDL, einen Fehlerzustand mit Savepoint und die
Groß- und Kleinschreibung von Bezeichnern. Jede Antwort landet als Zeile in
einer eigenen Ergebnistabelle.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus. Aufgabe 4 legt
absichtlich einen Fehlerzustand an; mit eingeschaltetem Auto rollback würde
pgAdmin die gesamte Transaktion vorzeitig zurückrollen.

Lege zu Beginn `tickets.pruefung` an. Sie hält die Antworten der fünf
Aufgaben fest:

```sql
DROP TABLE IF EXISTS tickets.pruefung;
CREATE TABLE tickets.pruefung (
    nr int PRIMARY KEY,
    ergebnis text
);
```

`ticket.subject` ist in diesem Schema `NOT NULL`. Für den Vergleich von
leerem Text und NULL in Aufgabe 1 richtest du deshalb eine eigene kleine
Tabelle ein, deren Textspalte NULL zulässt:

```sql
DROP TABLE IF EXISTS tickets.migrationstext;
CREATE TABLE tickets.migrationstext (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    wert text
);
```

## Aufgaben

1. Füge vier Testzeilen ein: drei mit einer leeren Zeichenkette und eine mit
   NULL. `id` ist `GENERATED ALWAYS AS IDENTITY`; ein eigener Nummernbereich
   ab 900001 braucht deshalb `OVERRIDING SYSTEM VALUE`, so wie bei einer
   Migration, die vorhandene Schlüssel aus einem Altsystem übernimmt:

   ```sql
   INSERT INTO tickets.migrationstext (id, wert) OVERRIDING SYSTEM VALUE VALUES
       (900001, ''),
       (900002, ''),
       (900003, ''),
       (900004, NULL);
   ```

   Zähle anschließend, wie viele Zeilen eine leere Zeichenkette und wie
   viele NULL enthalten, und schreibe das Ergebnis nach `tickets.pruefung`:

   ```sql
   INSERT INTO tickets.pruefung (nr, ergebnis)
   SELECT 1, 'leer=' || count(*) FILTER (WHERE wert = '')
              || ', null=' || count(*) FILTER (WHERE wert IS NULL)
   FROM tickets.migrationstext;
   ```

2. Vergleiche den Verkettungsoperator `||` mit der Funktion `concat()` für
   dieselbe NULL-Eingabe:

   ```sql
   INSERT INTO tickets.pruefung (nr, ergebnis)
   SELECT 2, 'verkettung=' || coalesce('a' || NULL, '<NULL>')
              || ', concat=' || coalesce(concat('a', NULL), '<NULL>');
   ```

3. Lege innerhalb einer Transaktion eine Tabelle an, füge eine Zeile ein und
   rolle beides zurück. Prüfe danach mit `to_regclass`, ob die Tabelle noch
   existiert:

   ```sql
   BEGIN;
   CREATE TABLE tickets.ddl_test (id integer PRIMARY KEY);
   INSERT INTO tickets.ddl_test VALUES (1);
   ROLLBACK;

   INSERT INTO tickets.pruefung (nr, ergebnis)
   VALUES (3, 'objekt_entfernt=' || (to_regclass('tickets.ddl_test') IS NULL)::text);
   ```

4. Lege `tickets.buchungstest` an und löse einen Fehler in einer laufenden
   Transaktion aus. Beobachte den SQLSTATE des Fehlers und den SQLSTATE der
   danach folgenden Anweisung, bevor du mit `ROLLBACK TO SAVEPOINT`
   fortsetzt:

   ```sql
   CREATE TABLE tickets.buchungstest (
       id integer PRIMARY KEY,
       betrag numeric(10, 2) NOT NULL
   );

   BEGIN;
   INSERT INTO tickets.buchungstest VALUES (1, 10.00);
   SAVEPOINT vor_fehler;
   INSERT INTO tickets.buchungstest VALUES (1, 20.00);
   SELECT count(*) FROM tickets.buchungstest;
   ROLLBACK TO SAVEPOINT vor_fehler;
   INSERT INTO tickets.buchungstest VALUES (2, 30.00);
   COMMIT;
   ```

   Die zweite `INSERT`-Anweisung verletzt den Primärschlüssel: SQLSTATE
   `23505`. Die folgende `SELECT`-Anweisung läuft in der bereits
   fehlgeschlagenen Transaktion und liefert deshalb SQLSTATE `25P02`, ohne
   selbst einen inhaltlichen Fehler zu haben. Erst `ROLLBACK TO SAVEPOINT`
   hebt den Fehlerzustand auf; die Einfügung vor dem Savepoint bleibt dabei
   erhalten. Schreibe den Endstand nach `tickets.pruefung`:

   ```sql
   INSERT INTO tickets.pruefung (nr, ergebnis)
   SELECT 4, string_agg(id || ':' || betrag, ', ' ORDER BY id)
   FROM tickets.buchungstest;
   ```

5. Lege eine Tabelle mit einem in Anführungszeichen geschriebenen,
   gemischt geschriebenen Namen an und greife anschließend ohne
   Anführungszeichen darauf zu:

   ```sql
   CREATE TABLE tickets."Kunde" (id integer PRIMARY KEY, name text);
   INSERT INTO tickets."Kunde" VALUES (1, 'Muster GmbH');

   SELECT * FROM tickets.kunde;
   ```

   Ohne Anführungszeichen faltet PostgreSQL den Namen auf Kleinschreibung.
   Die tatsächlich angelegte Tabelle heißt `Kunde`, nicht `kunde`; die
   Abfrage schlägt deshalb mit SQLSTATE `42P01` fehl. Schreibe fest, dass
   die Tabelle unter ihrem richtigen Namen weiterhin existiert:

   ```sql
   INSERT INTO tickets.pruefung (nr, ergebnis)
   VALUES (5, 'objekt_vorhanden=' || (to_regclass('tickets."Kunde"') IS NOT NULL)::text);
   ```

## Ergebnis prüfen

```sql
SELECT nr, ergebnis FROM tickets.pruefung ORDER BY nr;
```

Referenzlauf:

```text
 nr |          ergebnis           
----+-----------------------------
  1 | leer=3, null=1
  2 | verkettung=<NULL>, concat=a
  3 | objekt_entfernt=true
  4 | 1:10.00, 2:30.00
  5 | objekt_vorhanden=true
(5 rows)
```

## Hinweise

`count(spalte)` zählt nur Werte, die nicht NULL sind. Aufgabe 1 zeigt
denselben Mechanismus auf einer eigens angelegten Tabelle, weil `subject` in
`tickets.ticket` `NOT NULL` ist und dort keine NULL-Zeile eingefügt werden
kann. Bei entsprechenden Oracle-`VARCHAR2`-Spalten gilt die leere
Zeichenkette selbst als NULL; dieser Unterschied betrifft jede Migration
mit Textspalten aus Oracle.

`'a' || NULL` ergibt NULL: Der Operator `||` behandelt ein NULL-Argument wie
ein unbekanntes Ergebnis der gesamten Verkettung. `concat('a', NULL)`
ignoriert das NULL-Argument und liefert `a`. Beide Funktionen sind in
PostgreSQL vorhanden; welche zu einer bestehenden Anwendung passt, hängt von
der dort erwarteten Bedeutung von NULL ab.

`CREATE TABLE` innerhalb von `BEGIN` gehört unter PostgreSQL zur normalen
Transaktion und wird von `ROLLBACK` miterfasst. Unter Oracle 19c erzeugt
gültiges DDL ein implizites Commit vor und nach seiner Ausführung; ein
späteres Rollback nimmt eine bereits angelegte Tabelle dort nicht zurück.

SQLSTATE `25P02` bedeutet, dass die Transaktion bereits abgebrochen ist und
jede weitere Anweisung ignoriert wird, bis ein `ROLLBACK` oder ein
`ROLLBACK TO SAVEPOINT` folgt. Ein `SAVEPOINT` vor der riskanten Anweisung
erlaubt es, nur den Teil ab dem Savepoint zurückzunehmen und die
Transaktion danach fortzusetzen, ähnlich einem Oracle-Savepoint innerhalb
eines PL/SQL-Blocks.

Ein unquotierter Bezeichner wird unter PostgreSQL auf Kleinschreibung
gefaltet, unter Oracle dagegen auf Großschreibung. `"Kunde"` bewahrt die
Schreibweise, verlangt danach aber bei jedem Zugriff genau diese
Schreibweise in Anführungszeichen. Der Name `kunde` bezeichnet ein anderes,
nicht vorhandenes Objekt.

`loesung.sql` entfernt zu Beginn `tickets.pruefung`, `tickets.migrationstext`,
`tickets.ddl_test`, `tickets.buchungstest` und `tickets."Kunde"` und lässt
sich deshalb mehrfach ausführen.
