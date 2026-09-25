-- Musterlösung zu SQL-Übung 14. Der ausführbare Teil läuft im Query Tool
-- des RW-Servers und lässt sich wiederholen: Er entfernt zu Beginn
-- tickets.operation und legt die Tabelle neu an. Aufgabe 4 braucht zwei
-- Verbindungen und einen Switchover; die Schritte stehen als Kommentar,
-- jeweils mit Server und Query Tool, weil eine Skriptausführung nur eine
-- Verbindung hat.
-- Aufgabe 5 ist eine Entscheidung; die Lösung steht als Kommentar.
DROP TABLE IF EXISTS tickets.operation;

-- Aufgabe 1: Tabelle mit Operations-ID
-- Die Anwendung erzeugt operation_id einmal vor dem ersten Versuch und
-- sendet sie bei jeder Wiederholung mit. Der Primärschlüssel macht einen
-- zweiten Auftrag mit derselben ID unmöglich.
CREATE TABLE tickets.operation (
    operation_id uuid PRIMARY KEY,
    ticket_id bigint NOT NULL REFERENCES tickets.ticket (id),
    payload jsonb NOT NULL,
    executed_at timestamptz NOT NULL DEFAULT clock_timestamp()
);

-- Aufgabe 2: derselbe Auftrag zweimal
-- Erster Versuch: RETURNING liefert eine Zeile, INSERT 0 1.
INSERT INTO tickets.operation (operation_id, ticket_id, payload)
VALUES ('daf79a48-a152-47d4-9d92-3cca9782adf0', 1,
        '{"agent_id": 7}')
ON CONFLICT (operation_id) DO NOTHING
RETURNING operation_id;

-- Wiederholung mit derselben operation_id: keine Zeile, INSERT 0 0,
-- kein Fehler.
INSERT INTO tickets.operation (operation_id, ticket_id, payload)
VALUES ('daf79a48-a152-47d4-9d92-3cca9782adf0', 1,
        '{"agent_id": 7}')
ON CONFLICT (operation_id) DO NOTHING
RETURNING operation_id;

-- Wiederholung mit einer neuen operation_id: Der Primärschlüssel greift
-- nicht, es entsteht ein zweiter Auftrag mit derselben Nutzlast.
INSERT INTO tickets.operation (operation_id, ticket_id, payload)
VALUES ('4d45ac5a-e1a5-423c-ab40-38b15d424587', 1,
        '{"agent_id": 7}')
ON CONFLICT (operation_id) DO NOTHING
RETURNING operation_id;

SELECT count(*) AS operations,
       count(DISTINCT payload) AS distinct_payloads
FROM tickets.operation;
-- Ergebnis: operations 2, distinct_payloads 1. Beabsichtigt war ein
-- Auftrag. Die zweite Zeile stammt aus der Wiederholung mit neuer ID.

-- Aufgabe 3: derselbe Schlüssel, andere Nutzlast
-- ON CONFLICT DO NOTHING schweigt: keine Zeile, kein Fehler.
INSERT INTO tickets.operation (operation_id, ticket_id, payload)
VALUES ('daf79a48-a152-47d4-9d92-3cca9782adf0', 1,
        '{"agent_id": 12}')
ON CONFLICT (operation_id) DO NOTHING
RETURNING operation_id;

-- Erkennen: gespeicherte und gesendete Nutzlast vergleichen. jsonb
-- vergleicht den Inhalt, unabhängig von Leerzeichen und Reihenfolge der
-- Schlüssel.
SELECT a.payload AS stored,
       sent.payload AS sent,
       a.payload = sent.payload AS same_payload
FROM tickets.operation AS a
JOIN (VALUES ('daf79a48-a152-47d4-9d92-3cca9782adf0'::uuid,
              '{"agent_id": 12}'::jsonb))
     AS sent (operation_id, payload)
  ON sent.operation_id = a.operation_id;
-- Ergebnis: same_payload f. Das ist ein Programmfehler, weil dieselbe
-- ID für zwei verschiedene Aufträge steht. Die Anwendung meldet ihn und
-- wiederholt nicht. Gespeichert bleibt die Nutzlast des ersten Versuchs.

-- Aufgabe 4: eigene Verbindung bei einem Switchover beobachten
--
-- Beobachtungsabfrage, in je einem Query Tool auf RW- und RO-Server:
-- SELECT inet_server_addr() AS server_addr,
--        pg_postmaster_start_time() AS started_at,
--        pg_is_in_recovery() AS is_replica,
--        pg_backend_pid() AS pid;
-- -- RW: is_replica f. RO: is_replica t, andere server_addr.
--
-- Variante A, mit dem Recht, im eigenen Cluster umzuschalten:
--   kubectl -n training-postgres get pods -l cnpg.io/cluster=<cluster> -L role
--   kubectl cnpg promote <cluster> <replikat-pod> -n training-postgres
--   kubectl -n training-postgres get pods -l cnpg.io/cluster=<cluster> -L role -w
--
-- RW-Server, dasselbe Query Tool, nach dem Switchover:
-- -- Die Beobachtungsabfrage scheitert. Die alte Primärinstanz hat beim
-- -- Herunterfahren alle Verbindungen beendet:
-- -- FATAL: 57P01: terminating connection due to administrator command
-- -- Nach dem Neuverbinden: andere server_addr, is_replica f.
--
-- RO-Server, dasselbe Query Tool, nach dem Switchover:
-- -- War es mit dem beförderten Replikat verbunden, kann die Verbindung
-- -- bestehen bleiben: gleiche pid, is_replica jetzt f. Sonst bleibt
-- -- is_replica t.
--
-- Variante B, ohne dieses Recht: die eigene RW-Verbindung selbst beenden.
-- RW-Server, erstes Query Tool:
-- SET application_name = 'exercise14_a';
-- -- dann die Beobachtungsabfrage
--
-- RW-Server, zweites Query Tool:
-- SELECT pid, pg_terminate_backend(pid) AS terminated
-- FROM pg_stat_activity
-- WHERE application_name = 'exercise14_a'
--   AND usename = current_user;
-- -- terminated t
--
-- RW-Server, erstes Query Tool:
-- -- Die Beobachtungsabfrage scheitert mit derselben Meldung wie beim
-- -- Switchover:
-- -- FATAL: 57P01: terminating connection due to administrator command

-- Aufgabe 5: wiederholen oder nicht
-- 40001 serialization_failure, auch "conflict with recovery" auf dem
--       Replikat: ja, ganze Transaktion neu, mit Obergrenze.
-- 40P01 deadlock_detected: ja, ganze Transaktion neu, mit Obergrenze.
-- 57P01 admin_shutdown: ja, über eine neue Verbindung. Schreibarbeit nur,
--       wenn sie idempotent ist, etwa mit derselben operation_id.
-- 57P03 cannot_connect_now: ja, neue Verbindung nach kurzer Pause; der
--       Server fährt herunter oder startet gerade.
-- 08006 connection_failure oder Netzwerkfehler ohne SQLSTATE: ja, über
--       eine neue Verbindung, nur idempotente Arbeit. Ob ein COMMIT
--       ankam, ist offen.
-- 23505 unique_violation: nein. Derselbe Versuch scheitert wieder.
-- 23503 foreign_key_violation: nein. Das Ticket fehlt, ein fachlicher
--       Fehler.
-- 25006 read_only_sql_transaction: nein. Die Anwendung schreibt über
--       -ro oder -r und erreicht ein Replikat; die
--       Verbindungszeichenfolge prüfen.
-- 42P01 undefined_table: nein. Programm- oder Migrationsfehler.

-- Kontrolle
SELECT operation_id, count(*) OVER () AS operations, payload
FROM tickets.operation
ORDER BY executed_at;
-- Ergebnis: zwei Zeilen, operations 2, beide mit agent_id 7.
