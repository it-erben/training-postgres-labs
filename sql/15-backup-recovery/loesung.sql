-- Musterlösung zu SQL-Übung 15. Der ausführbare Teil läuft im Query Tool
-- des RW-Servers und lässt sich wiederholen: Er entfernt zu Beginn beide
-- Übungsschemas. Sicherung und Restore laufen über Dialoge des pgAdmin;
-- diese Schritte stehen als Kommentar an ihrer Stelle. Die Abfragen aus
-- Aufgabe 5 setzen den Restore voraus und stehen deshalb ebenfalls als
-- Kommentar am Ende.

-- Rücksetzen: Schemas aus einem früheren Lauf entfernen
DROP SCHEMA IF EXISTS restore_uebung, restore_uebung_defekt CASCADE;

-- Aufgabe 1: Ausgangsschema anlegen
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

SELECT count(*) AS tickets FROM restore_uebung.ticket;
-- Ergebnis: tickets 2

-- Aufgabe 2: Sicherung im pgAdmin
-- Objektbaum: Databases, app, Schemas, rechte Maustaste auf
-- restore_uebung, Backup...
-- Reiter General: Dateiname restore_uebung.dump, Format Custom, übrige
-- Felder unverändert. Backup starten.
-- Reiter Processes, Details des Auftrags: Der pg_dump-Aufruf enthält
-- unter anderem --file, --format=c und --schema restore_uebung.
-- Gleichwertig auf der Kommandozeile:
--   pg_dump "<verbindung>" -Fc -n restore_uebung -f restore_uebung.dump

-- Aufgabe 3: ein Ticket nach der Sicherung löschen
DELETE FROM restore_uebung.ticket WHERE id = 2;

-- Aufgabe 4: beschädigtes Schema umbenennen, Sicherung einspielen
ALTER SCHEMA restore_uebung RENAME TO restore_uebung_defekt;

-- Stand vor dem Restore: restore_uebung fehlt, der beschädigte Stand hat
-- ein Ticket.
SELECT to_regnamespace('restore_uebung') IS NULL AS restore_uebung_fehlt,
       (SELECT count(*) FROM restore_uebung_defekt.ticket) AS defekt;
-- Ergebnis: restore_uebung_fehlt t, defekt 1

-- Restore im pgAdmin:
-- Objektbaum: Schemas aktualisieren (rechte Maustaste, Refresh...).
-- Rechte Maustaste auf die Datenbank app, Restore...
-- Reiter General: Format Custom or tar, Dateiname restore_uebung.dump.
-- Restore starten. Gleichwertig auf der Kommandozeile:
--   pg_restore -d "<verbindung>" restore_uebung.dump
-- pg_restore legt das Schema restore_uebung mit Tabellen, Identity-
-- Sequenzen, Daten, Sequenzständen, Primär- und Fremdschlüsseln neu an.

-- Aufgabe 5: nach dem Restore vergleichen
-- Tickets beider Stände mit dem Namen des Agents:
-- SELECT 'gesichert' AS stand, t.id, t.subject, a.name
-- FROM restore_uebung.ticket AS t
-- JOIN restore_uebung.agent AS a ON a.id = t.agent_id
-- UNION ALL
-- SELECT 'defekt', t.id, t.subject, a.name
-- FROM restore_uebung_defekt.ticket AS t
-- JOIN restore_uebung_defekt.agent AS a ON a.id = t.agent_id
-- ORDER BY stand, id;
-- -- Ergebnis: defekt 1 Login Ada; gesichert 1 Login Ada,
-- -- 2 Bericht Linus. Im beschädigten Stand fehlt Ticket 2.
--
-- Sequenzen beider Schemas:
-- SELECT schemaname, sequencename, last_value
-- FROM pg_sequences
-- WHERE schemaname IN ('restore_uebung', 'restore_uebung_defekt')
-- ORDER BY schemaname, sequencename;
-- -- Ergebnis: alle vier Sequenzen mit last_value 2. Das nächste Ticket
-- -- in restore_uebung bekommt id 3. Ein DELETE setzt eine Sequenz nicht
-- -- zurück; der Restore setzt sie mit SEQUENCE SET auf den gesicherten
-- -- Stand.
--
-- Kontrolle:
-- SELECT (SELECT count(*) FROM restore_uebung.ticket) AS gesichert,
--        (SELECT count(*) FROM restore_uebung_defekt.ticket) AS defekt,
--        (SELECT last_value FROM restore_uebung.ticket_id_seq) AS sequenz;
-- -- Ergebnis: gesichert 2, defekt 1, sequenz 2
