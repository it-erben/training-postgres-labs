-- Musterlösung zu SQL-Übung 4. Der ausführbare Teil setzt Ticket 1 und 2
-- zurück und legt tickets.assignment_log neu an. Er lässt sich deshalb
-- mehrfach ausführen. Die eigentlichen Aufgaben brauchen zwei bis drei
-- gleichzeitige Verbindungen und stehen deshalb als Kommentar, markiert
-- mit -- Verbindung A, -- Verbindung B und -- Verbindung C.
UPDATE tickets.ticket SET agent_id = NULL WHERE id IN (1, 2);
DROP TABLE IF EXISTS tickets.assignment_log;
CREATE TABLE tickets.assignment_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ticket_id bigint NOT NULL,
    agent_id bigint NOT NULL,
    conn_label text NOT NULL
);

-- Aufgabe 1: Lost Update auf Ticket 1. Beide Verbindungen lesen
-- agent_id = NULL, bevor eine von ihnen schreibt.
--
-- Verbindung A:
-- SET application_name = 'exercise_a';
-- BEGIN;
-- SELECT id, agent_id FROM tickets.ticket WHERE id = 1;
--
-- Verbindung B (während A offen ist):
-- SET application_name = 'exercise_b';
-- BEGIN;
-- SELECT id, agent_id FROM tickets.ticket WHERE id = 1;
--
-- Verbindung A:
-- UPDATE tickets.ticket SET agent_id = 1 WHERE id = 1;
-- INSERT INTO tickets.assignment_log (ticket_id, agent_id, conn_label)
-- VALUES (1, 1, 'A');
-- COMMIT;
--
-- Verbindung B:
-- UPDATE tickets.ticket SET agent_id = 2 WHERE id = 1;
-- INSERT INTO tickets.assignment_log (ticket_id, agent_id, conn_label)
-- VALUES (1, 2, 'B');
-- COMMIT;
-- -- Beide UPDATE-Anweisungen laufen ohne Fehler durch. Ticket 1 zeigt am
-- -- Ende nur die Zuweisung von B. Die Zuweisung von A ist verloren.

-- Aufgabe 2: dieselbe Situation mit SELECT ... FOR UPDATE.
-- UPDATE tickets.ticket SET agent_id = NULL WHERE id = 1;
--
-- Verbindung A:
-- BEGIN;
-- SELECT id, agent_id FROM tickets.ticket WHERE id = 1 FOR UPDATE;
--
-- Verbindung B (während A offen ist, wartet auf die Zeilensperre):
-- BEGIN;
-- SELECT id, agent_id FROM tickets.ticket WHERE id = 1 FOR UPDATE;
--
-- Verbindung A:
-- UPDATE tickets.ticket SET agent_id = 1 WHERE id = 1;
-- INSERT INTO tickets.assignment_log (ticket_id, agent_id, conn_label)
-- VALUES (1, 1, 'A');
-- COMMIT;
--
-- Verbindung B (erhält jetzt agent_id = 1 aus der Zuweisung von A):
-- ROLLBACK;
-- -- B sieht die bereits erfolgte Zuweisung und weist selbst nicht mehr zu.

-- Aufgabe 3: bedingtes UPDATE auf Ticket 2, ohne vorheriges SELECT.
--
-- Verbindung A:
-- BEGIN;
-- UPDATE tickets.ticket SET agent_id = 1 WHERE id = 2 AND agent_id IS NULL;
-- -- UPDATE 1
--
-- Verbindung B (wartet auf dieselbe, durch A gesperrte Zeile):
-- BEGIN;
-- UPDATE tickets.ticket SET agent_id = 2 WHERE id = 2 AND agent_id IS NULL;
--
-- Verbindung A:
-- INSERT INTO tickets.assignment_log (ticket_id, agent_id, conn_label)
-- VALUES (2, 1, 'A');
-- COMMIT;
--
-- Verbindung B (Bedingung trifft auf die geänderte Zeile nicht mehr zu):
-- -- UPDATE 0
-- ROLLBACK;

-- Aufgabe 4: Schreibkonflikt unter SERIALIZABLE.
-- UPDATE tickets.ticket SET agent_id = NULL WHERE id = 1;
--
-- Verbindung A:
-- BEGIN ISOLATION LEVEL SERIALIZABLE;
-- SELECT id, agent_id FROM tickets.ticket WHERE id = 1;
-- UPDATE tickets.ticket SET agent_id = 1 WHERE id = 1 AND agent_id IS NULL;
--
-- Verbindung B (liest zuvor denselben Ausgangsstand, wartet dann):
-- BEGIN ISOLATION LEVEL SERIALIZABLE;
-- SELECT id, agent_id FROM tickets.ticket WHERE id = 1;
-- UPDATE tickets.ticket SET agent_id = 2 WHERE id = 1 AND agent_id IS NULL;
--
-- Verbindung A:
-- INSERT INTO tickets.assignment_log (ticket_id, agent_id, conn_label)
-- VALUES (1, 1, 'A');
-- COMMIT;
--
-- Verbindung B (die wartende UPDATE-Anweisung scheitert jetzt):
-- -- ERROR: 40001: could not serialize access due to concurrent update
-- ROLLBACK;

-- Aufgabe 5: wartende Sitzung finden. Aufgabe 4 bis zum Warten von B
-- wiederholen, A aber noch nicht committen. In einer dritten Verbindung C:
--
-- Verbindung C:
-- SELECT pid, application_name, state, wait_event_type, wait_event,
--        pg_blocking_pids(pid) AS blockers
-- FROM pg_stat_activity
-- WHERE application_name IN ('exercise_a', 'exercise_b')
-- ORDER BY application_name;
--
-- SELECT a.application_name, l.locktype, l.mode, l.granted
-- FROM pg_locks l
-- JOIN pg_stat_activity a ON a.pid = l.pid
-- WHERE a.application_name IN ('exercise_a', 'exercise_b')
-- ORDER BY a.application_name, l.mode;
-- -- B wartet mit wait_event_type=Lock, wait_event=transactionid. Die PID
-- -- von A erscheint als Blockierer. In pg_locks steht für B eine nicht
-- -- gewährte Sperre auf die Transaktions-ID von A.
--
-- Danach wie in Aufgabe 4 in A COMMIT und in B ROLLBACK ausführen.

-- Bonus: Deadlock.
-- UPDATE tickets.ticket SET agent_id = NULL WHERE id IN (1, 2);
--
-- Verbindung A:
-- BEGIN;
-- SELECT id FROM tickets.ticket WHERE id = 1 FOR UPDATE;
--
-- Verbindung B:
-- BEGIN;
-- SELECT id FROM tickets.ticket WHERE id = 2 FOR UPDATE;
--
-- Verbindung A (wartet auf B):
-- SELECT id FROM tickets.ticket WHERE id = 2 FOR UPDATE;
--
-- Verbindung B (fordert die von A gehaltene Zeile an, schließt den Zyklus):
-- SELECT id FROM tickets.ticket WHERE id = 1 FOR UPDATE;
-- -- Eine der beiden Sitzungen erhält SQLSTATE 40P01 (deadlock detected)
-- -- und muss ROLLBACK ausführen. Die andere kann fortfahren und abschließen.

-- Kontrolle nach den Aufgaben 1 bis 4 in der beschriebenen Reihenfolge
-- (Ergebnis siehe AUFGABE.md):
SELECT ticket_id, count(*) AS assignments FROM tickets.assignment_log
GROUP BY ticket_id ORDER BY ticket_id;
SELECT id, agent_id FROM tickets.ticket WHERE id IN (1, 2) ORDER BY id;
