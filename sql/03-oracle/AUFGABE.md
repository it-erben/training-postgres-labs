# SQL-Übung 3: Umstieg von Oracle

## Ziel

Du prüfst fünf Verhaltensunterschiede zwischen Oracle und PostgreSQL direkt
an deiner Datenbank: leere Zeichenketten vs. NULL, die Verkettung mit
NULL, den Rollback von DDL, einen Fehlerzustand mit Savepoint und die
Groß- und Kleinschreibung von Bezeichnern. Jede Antwort landet als Zeile in
einer eigenen Ergebnistabelle.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus. Aufgabe 4 legt
absichtlich einen Fehlerzustand an. Mit eingeschaltetem Auto rollback würde
pgAdmin die gesamte Transaktion vorzeitig zurückrollen. Führe jeden
Codeblock für sich aus: markieren, F5, dann der nächste Block.

Lege zu Beginn `tickets.verification` an. Sie hält die Antworten der fünf
Aufgaben fest:

```sql
DROP TABLE IF EXISTS tickets.verification;
CREATE TABLE tickets.verification (
    num int PRIMARY KEY,
    result text
);
```

`ticket.subject` ist in diesem Schema `NOT NULL`. Für den Vergleich von
leerem Text und NULL in Aufgabe 1 richtest du deshalb eine eigene kleine
Tabelle ein, deren Textspalte NULL zulässt:

```sql
DROP TABLE IF EXISTS tickets.migration_text;
CREATE TABLE tickets.migration_text (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    value text
);
```

## Aufgaben

1. Füge vier Testzeilen ein: drei mit einer leeren Zeichenkette und eine mit
   NULL. `id` ist `GENERATED ALWAYS AS IDENTITY`; ein eigener Nummernbereich
   ab 900001 braucht deshalb `OVERRIDING SYSTEM VALUE`, so wie bei einer
   Migration, die vorhandene Schlüssel aus einem Altsystem übernimmt:

   ```sql
   INSERT INTO tickets.migration_text (id, value) OVERRIDING SYSTEM VALUE VALUES
       (900001, ''),
       (900002, ''),
       (900003, ''),
       (900004, NULL);
   ```

   Zähle anschließend, wie viele Zeilen eine leere Zeichenkette und wie
   viele NULL enthalten, und schreibe das Ergebnis nach `tickets.verification`:

   ```sql
   INSERT INTO tickets.verification (num, result)
   SELECT 1, 'empty=' || count(*) FILTER (WHERE value = '')
              || ', null=' || count(*) FILTER (WHERE value IS NULL)
   FROM tickets.migration_text;
   ```

2. Vergleiche den Verkettungsoperator `||` mit der Funktion `concat()` für
   dieselbe NULL-Eingabe:

   ```sql
   INSERT INTO tickets.verification (num, result)
   SELECT 2, 'operator=' || coalesce('a' || NULL, '<NULL>')
              || ', concat=' || coalesce(concat('a', NULL), '<NULL>');
   ```

3. Lege innerhalb einer Transaktion eine Tabelle an, füge eine Zeile ein und
   rolle beides zurück. Markiere dafür nur diesen Block: `ROLLBACK` nimmt
   alles zurück, was in derselben Markierung davor steht.

   ```sql
   BEGIN;
   CREATE TABLE tickets.ddl_test (id integer PRIMARY KEY);
   INSERT INTO tickets.ddl_test VALUES (1);
   ROLLBACK;
   ```

   Prüfe danach mit `to_regclass`, ob die Tabelle noch existiert:

   ```sql
   INSERT INTO tickets.verification (num, result)
   VALUES (3, 'object_dropped=' || (to_regclass('tickets.ddl_test') IS NULL)::text);
   ```

4. Lege `tickets.booking_test` an und löse einen Fehler in einer laufenden
   Transaktion aus. Beobachte den SQLSTATE des Fehlers und den SQLSTATE der
   danach folgenden Anweisung, bevor du mit `ROLLBACK TO SAVEPOINT`
   fortsetzt. Tabelle anlegen, Transaktion und Savepoint öffnen:

   ```sql
   CREATE TABLE tickets.booking_test (
       id integer PRIMARY KEY,
       amount numeric(10, 2) NOT NULL
   );

   BEGIN;
   INSERT INTO tickets.booking_test VALUES (1, 10.00);
   SAVEPOINT before_error;
   ```

   Führe die beiden nächsten Anweisungen einzeln aus. Nach einem Fehler
   bricht die Ausführung der Markierung ab, eine zweite Anweisung darin
   liefe gar nicht mehr.

   ```sql
   INSERT INTO tickets.booking_test VALUES (1, 20.00);
   ```

   ```sql
   SELECT count(*) FROM tickets.booking_test;
   ```

   Die zweite `INSERT`-Anweisung verletzt den Primärschlüssel: SQLSTATE
   `23505`. Die folgende `SELECT`-Anweisung läuft in der bereits
   fehlgeschlagenen Transaktion und liefert deshalb SQLSTATE `25P02`, ohne
   selbst einen inhaltlichen Fehler zu haben. Erst `ROLLBACK TO SAVEPOINT`
   hebt den Fehlerzustand auf. Die Zeile, die vor dem Savepoint eingefügt
   wurde, bleibt dabei erhalten:

   ```sql
   ROLLBACK TO SAVEPOINT before_error;
   INSERT INTO tickets.booking_test VALUES (2, 30.00);
   COMMIT;
   ```

   Schreibe den Endstand nach `tickets.verification`:

   ```sql
   INSERT INTO tickets.verification (num, result)
   SELECT 4, string_agg(id || ':' || amount, ', ' ORDER BY id)
   FROM tickets.booking_test;
   ```

5. Lege eine Tabelle mit einem in Anführungszeichen geschriebenen,
   gemischt geschriebenen Namen an und greife anschließend ohne
   Anführungszeichen darauf zu:

   ```sql
   CREATE TABLE tickets."Customer" (id integer PRIMARY KEY, name text);
   INSERT INTO tickets."Customer" VALUES (1, 'Muster GmbH');
   ```

   Führe die Abfrage in einer eigenen Ausführung aus. In derselben
   Markierung würde ihr Fehler auch das `CREATE TABLE` zurückrollen.

   ```sql
   SELECT * FROM tickets.customer;
   ```

   Ohne Anführungszeichen faltet PostgreSQL den Namen auf Kleinschreibung.
   Angelegt wurde aber `Customer` mit großem C, deshalb schlägt die Abfrage mit
   SQLSTATE `42P01` fehl. Halte fest, dass die Tabelle unter ihrem richtigen
   Namen weiterhin existiert:

   ```sql
   INSERT INTO tickets.verification (num, result)
   VALUES (5, 'object_exists=' || (to_regclass('tickets."Customer"') IS NOT NULL)::text);
   ```

## Ergebnis prüfen

```sql
SELECT num, result FROM tickets.verification ORDER BY num;
```

Referenzlauf:

```text
 num |          result           
-----+---------------------------
   1 | empty=3, null=1
   2 | operator=<NULL>, concat=a
   3 | object_dropped=true
   4 | 1:10.00, 2:30.00
   5 | object_exists=true
(5 rows)
```

## Hinweise

Aufgabe 1 zählt mit `count(*) FILTER (WHERE ...)`, also Zeilen, für die
die Bedingung zutrifft. `value = ''` ist für die NULL-Zeile nicht wahr,
deshalb zählt nur `value IS NULL` sie mit. `count(value)` ergäbe 3, weil
`count(spalte)` NULL-Werte überspringt. Die Tabelle ist eigens angelegt,
weil `subject` in `tickets.ticket` `NOT NULL` ist und dort keine NULL-Zeile
eingefügt werden kann. In einer Oracle-`VARCHAR2`-Spalte ist die leere
Zeichenkette selbst NULL. Das betrifft jede Migration mit Textspalten aus
Oracle.

`'a' || NULL` ergibt NULL. Ist ein Argument von `||` NULL, ist die gesamte
Verkettung unbekannt. `concat('a', NULL)` ignoriert das NULL-Argument und
liefert `a`. PostgreSQL hat beide. Welche Variante zu einer bestehenden
Anwendung passt, hängt davon ab, was NULL dort bedeuten soll.

`CREATE TABLE` innerhalb von `BEGIN` gehört unter PostgreSQL zur normalen
Transaktion, `ROLLBACK` nimmt es mit zurück. Unter Oracle 19c löst gültiges
DDL vor und nach seiner Ausführung ein implizites Commit aus. Ein späteres
Rollback nimmt eine bereits angelegte Tabelle dort nicht zurück.

SQLSTATE `25P02` bedeutet, dass die Transaktion bereits abgebrochen ist und
jede weitere Anweisung ignoriert wird, bis ein `ROLLBACK` oder ein
`ROLLBACK TO SAVEPOINT` folgt. Mit einem `SAVEPOINT` vor der riskanten
Anweisung lässt sich nur der Teil ab dem Savepoint zurücknehmen, danach
läuft die Transaktion weiter. Ein Oracle-Savepoint in einem PL/SQL-Block
funktioniert ähnlich.

PostgreSQL faltet einen unquotierten Bezeichner auf Kleinschreibung, Oracle
auf Großschreibung. `"Customer"` bewahrt die
Schreibweise, verlangt danach aber bei jedem Zugriff genau diese
Schreibweise in Anführungszeichen. Der Name `customer` bezeichnet ein anderes,
nicht vorhandenes Objekt.

`loesung.sql` läuft abschnittsweise: Jeder Block ab `-- Abschnitt` wird
einzeln markiert und ausgeführt. Die drei Anweisungen mit erwartetem Fehler
stehen dort als Kommentar mit ihrem SQLSTATE. Abschnitt 1 entfernt
`tickets.verification`, `tickets.migration_text`, `tickets.ddl_test`,
`tickets.booking_test` und `tickets."Customer"`. Die Datei lässt sich deshalb
mehrfach ausführen.
