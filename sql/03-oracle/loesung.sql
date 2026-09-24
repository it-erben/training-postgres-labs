-- Musterlösung zu SQL-Übung 3. Läuft vollständig im Query Tool und lässt
-- sich wiederholen: Die ersten Anweisungen entfernen alle Übungsobjekte.
DROP TABLE IF EXISTS tickets.pruefung;
DROP TABLE IF EXISTS tickets.migrationstext;
DROP TABLE IF EXISTS tickets.ddl_test;
DROP TABLE IF EXISTS tickets.buchungstest;
DROP TABLE IF EXISTS tickets."Kunde";

CREATE TABLE tickets.pruefung (
    nr int PRIMARY KEY,
    ergebnis text
);

-- Aufgabe 1: leere Zeichenkette gegenüber NULL. subject in tickets.ticket
-- ist NOT NULL, deshalb eine eigene Tabelle mit vier Testzeilen.
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

-- Aufgabe 2: Verkettungsoperator gegenüber concat() bei NULL
INSERT INTO tickets.pruefung (nr, ergebnis)
SELECT 2, 'verkettung=' || coalesce('a' || NULL, '<NULL>')
           || ', concat=' || coalesce(concat('a', NULL), '<NULL>');

-- Aufgabe 3: CREATE TABLE im Rollback. PostgreSQL nimmt DDL innerhalb
-- eines Transaktionsblocks vollständig zurück.
BEGIN;
CREATE TABLE tickets.ddl_test (id integer PRIMARY KEY);
INSERT INTO tickets.ddl_test VALUES (1);
ROLLBACK;

INSERT INTO tickets.pruefung (nr, ergebnis)
VALUES (3, 'objekt_entfernt=' || (to_regclass('tickets.ddl_test') IS NULL)::text);

-- Aufgabe 4: Fehler und Savepoint. Die zweite Einfügung verletzt den
-- Primärschlüssel (23505); die folgende Abfrage läuft im Fehlerzustand
-- der Transaktion (25P02). ROLLBACK TO SAVEPOINT hebt ihn auf und erhält
-- die Einfügung vor dem Savepoint.
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

INSERT INTO tickets.pruefung (nr, ergebnis)
SELECT 4, string_agg(id || ':' || betrag, ', ' ORDER BY id)
FROM tickets.buchungstest;

-- Aufgabe 5: Bezeichner. "Kunde" bewahrt die Schreibweise; der
-- unquotierte Zugriff über kunde faltet auf Kleinschreibung und findet
-- deshalb kein Objekt (42P01).
CREATE TABLE tickets."Kunde" (id integer PRIMARY KEY, name text);
INSERT INTO tickets."Kunde" VALUES (1, 'Muster GmbH');

SELECT * FROM tickets.kunde;

INSERT INTO tickets.pruefung (nr, ergebnis)
VALUES (5, 'objekt_vorhanden=' || (to_regclass('tickets."Kunde"') IS NOT NULL)::text);

-- Kontrolle
SELECT nr, ergebnis FROM tickets.pruefung ORDER BY nr;
