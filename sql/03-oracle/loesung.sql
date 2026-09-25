-- Musterlösung zu SQL-Übung 3. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Anweisungen mit erwartetem Fehler (23505, 25P02, 42P01) stehen
-- als Kommentar mit ihrem SQLSTATE. Im Query Tool werden sie jeweils allein
-- markiert und ausgeführt. Die Datei verträgt beliebig viele Läufe, weil der
-- erste Abschnitt alle Übungsobjekte entfernt.

-- Abschnitt 1: Rücksetzen und Ergebnistabelle
DROP TABLE IF EXISTS tickets.verification;
DROP TABLE IF EXISTS tickets.migration_text;
DROP TABLE IF EXISTS tickets.ddl_test;
DROP TABLE IF EXISTS tickets.booking_test;
DROP TABLE IF EXISTS tickets."Customer";

CREATE TABLE tickets.verification (
    num int PRIMARY KEY,
    result text
);

-- Abschnitt 2 (Aufgabe 1): leere Zeichenkette gegenüber NULL. subject in
-- tickets.ticket ist NOT NULL, deshalb eine eigene Tabelle mit vier
-- Testzeilen.
CREATE TABLE tickets.migration_text (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    value text
);
INSERT INTO tickets.migration_text (id, value) OVERRIDING SYSTEM VALUE VALUES
    (900001, ''),
    (900002, ''),
    (900003, ''),
    (900004, NULL);

INSERT INTO tickets.verification (num, result)
SELECT 1, 'empty=' || count(*) FILTER (WHERE value = '')
           || ', null=' || count(*) FILTER (WHERE value IS NULL)
FROM tickets.migration_text;

-- Abschnitt 3 (Aufgabe 2): Verkettungsoperator gegenüber concat() bei NULL
INSERT INTO tickets.verification (num, result)
SELECT 2, 'operator=' || coalesce('a' || NULL, '<NULL>')
           || ', concat=' || coalesce(concat('a', NULL), '<NULL>');

-- Abschnitt 4 (Aufgabe 3): CREATE TABLE im Rollback. PostgreSQL nimmt DDL
-- innerhalb eines Transaktionsblocks vollständig zurück. Der Abschnitt
-- steht allein: ROLLBACK nimmt alles zurück, was in derselben Markierung
-- davor steht.
BEGIN;
CREATE TABLE tickets.ddl_test (id integer PRIMARY KEY);
INSERT INTO tickets.ddl_test VALUES (1);
ROLLBACK;

-- Abschnitt 5 (Aufgabe 3): Prüfen, ob die Tabelle noch existiert
INSERT INTO tickets.verification (num, result)
VALUES (3, 'object_dropped=' || (to_regclass('tickets.ddl_test') IS NULL)::text);

-- Abschnitt 6 (Aufgabe 4): Tabelle anlegen, Transaktion mit Savepoint öffnen
CREATE TABLE tickets.booking_test (
    id integer PRIMARY KEY,
    amount numeric(10, 2) NOT NULL
);

BEGIN;
INSERT INTO tickets.booking_test VALUES (1, 10.00);
SAVEPOINT before_error;

-- Aufgabe 4, erwartete Fehler, im Query Tool jeweils allein ausführen:
-- INSERT INTO tickets.booking_test VALUES (1, 20.00);
-- -- SQLSTATE 23505: doppelter Primärschlüssel
-- SELECT count(*) FROM tickets.booking_test;
-- -- SQLSTATE 25P02: Die Transaktion ist bereits abgebrochen.

-- Abschnitt 7 (Aufgabe 4): ROLLBACK TO SAVEPOINT hebt den Fehlerzustand auf
-- und erhält die Einfügung vor dem Savepoint.
ROLLBACK TO SAVEPOINT before_error;
INSERT INTO tickets.booking_test VALUES (2, 30.00);
COMMIT;

INSERT INTO tickets.verification (num, result)
SELECT 4, string_agg(id || ':' || amount, ', ' ORDER BY id)
FROM tickets.booking_test;

-- Abschnitt 8 (Aufgabe 5): Bezeichner. "Customer" bewahrt die Schreibweise.
CREATE TABLE tickets."Customer" (id integer PRIMARY KEY, name text);
INSERT INTO tickets."Customer" VALUES (1, 'Muster GmbH');

-- Aufgabe 5, erwarteter Fehler, im Query Tool allein ausführen. Der
-- unquotierte Name customer faltet auf Kleinschreibung und findet kein Objekt:
-- SELECT * FROM tickets.customer;
-- -- SQLSTATE 42P01: relation "tickets.customer" does not exist

-- Abschnitt 9 (Aufgabe 5): Tabelle unter ihrem richtigen Namen prüfen
INSERT INTO tickets.verification (num, result)
VALUES (5, 'object_exists=' || (to_regclass('tickets."Customer"') IS NOT NULL)::text);

-- Abschnitt 10: Kontrolle
SELECT num, result FROM tickets.verification ORDER BY num;
