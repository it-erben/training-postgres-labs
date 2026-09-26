-- Musterlösung zu SQL-Übung 5. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Abschnitt 1 entfernt die Zieltabelle und legt legacy_ticket
-- wie im Ausgangsstand der Aufgabe neu an, die Datei lässt sich deshalb
-- wiederholen.

-- Abschnitt 1: Ausgangsstand
DROP TABLE IF EXISTS tickets.ticket_new;
DROP TABLE IF EXISTS tickets.legacy_ticket;
CREATE TABLE tickets.legacy_ticket (
    id bigint,
    subject text,
    closed smallint,
    created_at timestamp,
    amount double precision
);
INSERT INTO tickets.legacy_ticket VALUES
    (101, 'Login schlägt fehl',    0, '2026-08-21 12:00:00',   19.99),
    (102, 'Rechnung doppelt',      1, '2025-12-31 23:30:00',    0.1),
    (103, 'Export bricht ab',      1, '2025-03-30 00:30:00',    0.2),
    (104, 'Passwort zurücksetzen', 0, '2025-03-30 02:30:00',    0.3),
    (105, 'Kontoauszug fehlt',     1, '2026-06-30 22:15:00', 1250),
    (106, 'Adresse ändern',        0, '2026-01-15 08:00:00',    4.6);

-- Abschnitt 2 (Aufgabe 1): Zieltabelle mit PostgreSQL-Typen
CREATE TABLE tickets.ticket_new (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    legacy_id bigint UNIQUE NOT NULL,
    subject text NOT NULL,
    is_closed boolean NOT NULL,
    created_at timestamptz NOT NULL,
    amount numeric(12,2) NOT NULL
);

-- Abschnitt 3 (Aufgabe 2): Übernahme. created_at ist UTC-Wandzeit; AT TIME
-- ZONE 'UTC' macht daraus einen Zeitpunkt unabhängig von der
-- Sitzungszeitzone.
INSERT INTO tickets.ticket_new (legacy_id, subject, is_closed, created_at, amount)
SELECT id, subject, closed = 1, created_at AT TIME ZONE 'UTC', round(amount::numeric, 2)
FROM tickets.legacy_ticket
ORDER BY id;

-- Abschnitt 4 (Aufgabe 3): Sitzungszeitzone UTC
SET TimeZone = 'UTC';

-- Abschnitt 5 (Aufgabe 3): Ein bloßer Cast deutet die Wandzeit in der
-- Sitzungszeitzone. Unter UTC ist die Abweichung 00:00:00.
SELECT id, created_at,
       created_at::timestamptz AS cast_result,
       created_at AT TIME ZONE 'UTC' AS instant_utc,
       created_at::timestamptz - created_at AT TIME ZONE 'UTC' AS deviation
FROM tickets.legacy_ticket
ORDER BY id;

-- Abschnitt 6 (Aufgabe 3): Sitzungszeitzone Europe/Berlin
SET TimeZone = 'Europe/Berlin';

-- Abschnitt 7 (Aufgabe 3): Unter Europe/Berlin weicht der Cast um -01:00:00
-- oder -02:00:00 ab.
SELECT id, created_at,
       created_at::timestamptz AS cast_result,
       created_at AT TIME ZONE 'UTC' AS instant_utc,
       created_at::timestamptz - created_at AT TIME ZONE 'UTC' AS deviation
FROM tickets.legacy_ticket
ORDER BY id;

-- Abschnitt 8 (Aufgabe 3): Zeitzone zurücksetzen
RESET TimeZone;

-- Abschnitt 9 (Aufgabe 4): Summen in beiden Typen
SELECT sum(amount) AS numeric_sum,
       (SELECT sum(amount) FROM tickets.legacy_ticket) AS double_sum
FROM tickets.ticket_new;
