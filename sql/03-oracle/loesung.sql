-- Musterlösung zu SQL-Übung 3. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Anweisungen mit erwartetem Fehler (23505, 25P02, 42P01) stehen
-- als Kommentar mit ihrem SQLSTATE. Im Query Tool werden sie jeweils allein
-- markiert und ausgeführt. Die Datei verträgt beliebig viele Läufe, weil der
-- erste Abschnitt alle Übungsobjekte entfernt.

-- Abschnitt 1: Rücksetzen und Ergebnistabelle
DROP TABLE IF EXISTS tickets.pruefung;
DROP TABLE IF EXISTS tickets.migrationstext;
DROP TABLE IF EXISTS tickets.ddl_test;
DROP TABLE IF EXISTS tickets.buchungstest;
DROP TABLE IF EXISTS tickets."Kunde";

CREATE TABLE tickets.pruefung (
    nr int PRIMARY KEY,
    ergebnis text
);

-- Abschnitt 2 (Aufgabe 1): leere Zeichenkette gegenüber NULL. subject in
-- tickets.ticket ist NOT NULL, deshalb eine eigene Tabelle mit vier
-- Testzeilen.
CREATE TABLE tickets.migrationstext (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    wert text
);
INSERT INTO tickets.migrationstext (id, wert) OVERRIDING SYSTEM VALUE VALUES
    (900001, ''),
    (900002, ''),
    (900003, ''),
    (900004, NULL);

INSERT INTO tickets.pruefung (nr, ergebnis)
SELECT 1, 'leer=' || count(*) FILTER (WHERE wert = '')
           || ', null=' || count(*) FILTER (WHERE wert IS NULL)
FROM tickets.migrationstext;

-- Abschnitt 3 (Aufgabe 2): Verkettungsoperator gegenüber concat() bei NULL
INSERT INTO tickets.pruefung (nr, ergebnis)
SELECT 2, 'verkettung=' || coalesce('a' || NULL, '<NULL>')
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
INSERT INTO tickets.pruefung (nr, ergebnis)
VALUES (3, 'objekt_entfernt=' || (to_regclass('tickets.ddl_test') IS NULL)::text);

-- Abschnitt 6 (Aufgabe 4): Tabelle anlegen, Transaktion mit Savepoint öffnen
CREATE TABLE tickets.buchungstest (
    id integer PRIMARY KEY,
    betrag numeric(10, 2) NOT NULL
);

BEGIN;
INSERT INTO tickets.buchungstest VALUES (1, 10.00);
SAVEPOINT vor_fehler;

-- Aufgabe 4, erwartete Fehler, im Query Tool jeweils allein ausführen:
-- INSERT INTO tickets.buchungstest VALUES (1, 20.00);
-- -- SQLSTATE 23505: doppelter Primärschlüssel
-- SELECT count(*) FROM tickets.buchungstest;
-- -- SQLSTATE 25P02: Die Transaktion ist bereits abgebrochen.

-- Abschnitt 7 (Aufgabe 4): ROLLBACK TO SAVEPOINT hebt den Fehlerzustand auf
-- und erhält die Einfügung vor dem Savepoint.
ROLLBACK TO SAVEPOINT vor_fehler;
INSERT INTO tickets.buchungstest VALUES (2, 30.00);
COMMIT;

INSERT INTO tickets.pruefung (nr, ergebnis)
SELECT 4, string_agg(id || ':' || betrag, ', ' ORDER BY id)
FROM tickets.buchungstest;

-- Abschnitt 8 (Aufgabe 5): Bezeichner. "Kunde" bewahrt die Schreibweise.
CREATE TABLE tickets."Kunde" (id integer PRIMARY KEY, name text);
INSERT INTO tickets."Kunde" VALUES (1, 'Muster GmbH');

-- Aufgabe 5, erwarteter Fehler, im Query Tool allein ausführen. Der
-- unquotierte Name kunde faltet auf Kleinschreibung und findet kein Objekt:
-- SELECT * FROM tickets.kunde;
-- -- SQLSTATE 42P01: relation "tickets.kunde" does not exist

-- Abschnitt 9 (Aufgabe 5): Tabelle unter ihrem richtigen Namen prüfen
INSERT INTO tickets.pruefung (nr, ergebnis)
VALUES (5, 'objekt_vorhanden=' || (to_regclass('tickets."Kunde"') IS NOT NULL)::text);

-- Abschnitt 10: Kontrolle
SELECT nr, ergebnis FROM tickets.pruefung ORDER BY nr;
