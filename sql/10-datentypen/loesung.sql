-- Musterlösung zu SQL-Übung 10. Läuft vollständig im Query Tool und
-- lässt sich wiederholen: Die ersten Anweisungen entfernen die Zieltabelle
-- und legen legacy_ticket wie im Ausgangsstand der Aufgabe neu an.
DROP TABLE IF EXISTS tickets.ticket_neu;

-- Ausgangsstand
DROP TABLE IF EXISTS tickets.legacy_ticket;
CREATE TABLE tickets.legacy_ticket (
    id bigint,
    subject text,
    closed smallint,
    created_at timestamp,
    betrag double precision
);
INSERT INTO tickets.legacy_ticket VALUES
    (101, 'Login schlägt fehl',    0, '2026-08-21 12:00:00',   19.99),
    (102, 'Rechnung doppelt',      1, '2025-12-31 23:30:00',    0.1),
    (103, 'Export bricht ab',      1, '2025-03-30 00:30:00',    0.2),
    (104, 'Passwort zurücksetzen', 0, '2025-03-30 02:30:00',    0.3),
    (105, 'Kontoauszug fehlt',     1, '2026-06-30 22:15:00', 1250),
    (106, 'Adresse ändern',        0, '2026-01-15 08:00:00',    4.6);

-- Aufgabe 1: Zieltabelle mit PostgreSQL-Typen
CREATE TABLE tickets.ticket_neu (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    legacy_id bigint UNIQUE NOT NULL,
    subject text NOT NULL,
    is_closed boolean NOT NULL,
    created_at timestamptz NOT NULL,
    betrag numeric(12,2) NOT NULL
);

-- Aufgabe 2: Übernahme. created_at ist UTC-Wandzeit; AT TIME ZONE 'UTC'
-- macht daraus einen Zeitpunkt unabhängig von der Sitzungszeitzone.
INSERT INTO tickets.ticket_neu (legacy_id, subject, is_closed, created_at, betrag)
SELECT id, subject, closed = 1, created_at AT TIME ZONE 'UTC', round(betrag::numeric, 2)
FROM tickets.legacy_ticket
ORDER BY id;

-- Aufgabe 3: Ein bloßer Cast deutet die Wandzeit in der Sitzungszeitzone.
-- Unter UTC ist die Abweichung 00:00:00, unter Europe/Berlin -01:00:00 oder -02:00:00.
SET TimeZone = 'UTC';
SELECT id, created_at,
       created_at::timestamptz AS cast_ergebnis,
       created_at AT TIME ZONE 'UTC' AS zeitpunkt_utc,
       created_at::timestamptz - created_at AT TIME ZONE 'UTC' AS abweichung
FROM tickets.legacy_ticket
ORDER BY id;
SET TimeZone = 'Europe/Berlin';
SELECT id, created_at,
       created_at::timestamptz AS cast_ergebnis,
       created_at AT TIME ZONE 'UTC' AS zeitpunkt_utc,
       created_at::timestamptz - created_at AT TIME ZONE 'UTC' AS abweichung
FROM tickets.legacy_ticket
ORDER BY id;
RESET TimeZone;

-- Aufgabe 4: Summen in beiden Typen
SELECT sum(betrag) AS numeric_summe,
       (SELECT sum(betrag) FROM tickets.legacy_ticket) AS double_summe
FROM tickets.ticket_neu;
