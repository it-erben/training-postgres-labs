-- Musterlösung zu SQL-Übung 15. Der ausführbare Teil läuft im Query Tool
-- des RW-Servers und lässt sich wiederholen: Er entfernt zu Beginn beide
-- Übungsschemas. Sicherung und Restore laufen über Dialoge des pgAdmin;
-- diese Schritte stehen als Kommentar an ihrer Stelle. Die Abfragen aus
-- Aufgabe 5 setzen den Restore voraus und stehen deshalb ebenfalls als
-- Kommentar am Ende.

-- Rücksetzen: Schemas aus einem früheren Lauf entfernen
DROP SCHEMA IF EXISTS restore_exercise, restore_exercise_broken CASCADE;

-- Aufgabe 1: Ausgangsschema anlegen
CREATE SCHEMA restore_exercise;
CREATE TABLE restore_exercise.agent (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL
);
CREATE TABLE restore_exercise.ticket (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    agent_id bigint NOT NULL REFERENCES restore_exercise.agent,
    subject text NOT NULL
);
INSERT INTO restore_exercise.agent (name) VALUES ('Ada'), ('Linus');
INSERT INTO restore_exercise.ticket (agent_id, subject)
VALUES (1, 'Login'), (2, 'Bericht');

SELECT count(*) AS tickets FROM restore_exercise.ticket;
-- Ergebnis: tickets 2

-- Aufgabe 2: Sicherung im pgAdmin
-- Objektbaum: Databases, app, Schemas, rechte Maustaste auf
-- restore_exercise, Backup...
-- Reiter General: Dateiname restore_exercise.dump, Format Custom, übrige
-- Felder unverändert. Backup starten.
-- Reiter Processes, Details des Auftrags: Laut Quelltext von pgAdmin
-- enthält der pg_dump-Aufruf unter anderem --file, --format=c und
-- --schema restore_exercise. Die genaue Befehlszeile zeigt der Reiter in
-- der Kursumgebung; Menünamen können dort leicht abweichen.
-- Gleichwertig auf der Kommandozeile:
--   pg_dump "<verbindung>" -Fc -n restore_exercise -f restore_exercise.dump

-- Aufgabe 3: ein Ticket nach der Sicherung löschen
DELETE FROM restore_exercise.ticket WHERE id = 2;

-- Aufgabe 4: beschädigtes Schema umbenennen, Sicherung einspielen
ALTER SCHEMA restore_exercise RENAME TO restore_exercise_broken;

-- Stand vor dem Restore: restore_exercise fehlt, der beschädigte Stand hat
-- ein Ticket.
SELECT to_regnamespace('restore_exercise') IS NULL AS restore_exercise_missing,
       (SELECT count(*) FROM restore_exercise_broken.ticket) AS broken;
-- Ergebnis: restore_exercise_missing t, broken 1

-- Restore im pgAdmin:
-- Objektbaum: Schemas aktualisieren (rechte Maustaste, Refresh...).
-- Rechte Maustaste auf die Datenbank app, Restore...
-- Reiter General: Format Custom or tar, Dateiname restore_exercise.dump.
-- Restore starten. Gleichwertig auf der Kommandozeile:
--   pg_restore -d "<verbindung>" restore_exercise.dump
-- pg_restore legt das Schema restore_exercise mit Tabellen, Identity-
-- Sequenzen, Daten, Sequenzständen, Primär- und Fremdschlüsseln neu an.

-- Aufgabe 5: nach dem Restore vergleichen
-- Tickets beider Stände mit dem Namen des Agents:
-- SELECT 'backed_up' AS source, t.id, t.subject, a.name
-- FROM restore_exercise.ticket AS t
-- JOIN restore_exercise.agent AS a ON a.id = t.agent_id
-- UNION ALL
-- SELECT 'broken', t.id, t.subject, a.name
-- FROM restore_exercise_broken.ticket AS t
-- JOIN restore_exercise_broken.agent AS a ON a.id = t.agent_id
-- ORDER BY source, id;
-- -- Ergebnis: backed_up 1 Login Ada, 2 Bericht Linus; broken 1 Login
-- -- Ada. Im beschädigten Stand fehlt Ticket 2.
--
-- Sequenzen beider Schemas:
-- SELECT schemaname, sequencename, last_value
-- FROM pg_sequences
-- WHERE schemaname IN ('restore_exercise', 'restore_exercise_broken')
-- ORDER BY schemaname, sequencename;
-- -- Ergebnis: alle vier Sequenzen mit last_value 2. Das nächste Ticket
-- -- in restore_exercise bekommt id 3. Ein DELETE setzt eine Sequenz nicht
-- -- zurück; der Restore setzt sie mit SEQUENCE SET auf den gesicherten
-- -- Stand.
--
-- Kontrolle:
-- SELECT (SELECT count(*) FROM restore_exercise.ticket) AS backed_up,
--        (SELECT count(*) FROM restore_exercise_broken.ticket) AS broken,
--        (SELECT last_value FROM restore_exercise.ticket_id_seq) AS seq;
-- -- Ergebnis: backed_up 2, broken 1, seq 2
