-- Musterlösung zu SQL-Übung 12. Der ausführbare Teil läuft abschnittsweise
-- im Query Tool des RW-Servers: jeden mit "-- Abschnitt" beginnenden Block
-- einzeln markieren und mit F5 ausführen. Er lässt sich wiederholen, weil
-- der erste Abschnitt tickets.read_test entfernt und der vierte sie neu
-- anlegt. Anweisungen für den RO-Server stehen als Kommentarblock, markiert
-- mit -- RO-Server, weil eine Skriptausführung nur eine Verbindung hat.
-- Aufgabe 4 und 5 sind Beobachtung und Begründung; ihre Lösung steht als
-- Kommentar am Ende.

-- Abschnitt 1: Übungstabelle entfernen
DROP TABLE IF EXISTS tickets.read_test;

-- Abschnitt 2 (Aufgabe 1): Primärinstanz und Replikat unterscheiden,
-- RW-Server
SELECT pg_is_in_recovery() AS is_replica,
       inet_server_addr() AS server_addr,
       current_setting('transaction_read_only') AS read_only;
-- Ergebnis: is_replica f, read_only off.

-- RO-Server
-- SELECT pg_is_in_recovery() AS is_replica,
--        inet_server_addr() AS server_addr,
--        current_setting('transaction_read_only') AS read_only;
-- -- Ergebnis: is_replica t, read_only on, eine andere server_addr.

-- Abschnitt 3 (Aufgabe 2): Verschlüsselung der eigenen Verbindung,
-- RW-Server
SELECT ssl, version, cipher, bits
FROM pg_stat_ssl
WHERE pid = pg_backend_pid();
-- Ergebnis: ssl t. Dieselbe Abfrage auf dem RO-Server zeigt die
-- Verbindung zum Replikat.

-- Abschnitt 4 (Aufgabe 3): Schreiben auf RW, sofort lesen auf RO. Die drei
-- Anweisungen laufen als eine Ausführung, Data Output zeigt die Zeile mit
-- der WAL-Position.
CREATE TABLE tickets.read_test (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    note text NOT NULL,
    written_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO tickets.read_test (note) VALUES ('geschrieben auf RW');
SELECT id, note, written_at, pg_current_wal_lsn() AS wal_position_rw
FROM tickets.read_test;

-- RO-Server, direkt danach
-- SELECT id, note, written_at FROM tickets.read_test;
-- SELECT pg_last_wal_replay_lsn() AS replayed_up_to,
--        pg_last_xact_replay_timestamp() AS last_replayed_xact;
-- -- last_replayed_xact ist der Commit-Zeitpunkt auf der Primärinstanz,
-- -- kurz nach written_at; auf trainer-pg lagen 3 bis 7 Millisekunden
-- -- dazwischen, einschließlich der Dauer von CREATE TABLE und INSERT. Die
-- -- Zeile ist sichtbar, sobald replayed_up_to die wal_position_rw
-- -- erreicht hat.
--
-- RO-Server, Schreibversuch
-- INSERT INTO tickets.read_test (note) VALUES ('geschrieben auf RO');
-- -- ERROR: 25006: cannot execute INSERT in a read-only transaction

-- Aufgabe 4: Cluster-Status, außerhalb der Datenbank
-- Variante kubectl:
--   kubectl -n training-postgres get cluster <cluster>
--   Spalten STATUS (Phase), PRIMARY (Primärinstanz), INSTANCES und READY.
--   kubectl -n training-postgres get pods -l cnpg.io/cluster=<cluster> -L cnpg.io/instanceRole -o wide
--   Spalte INSTANCEROLE: eine Instanz primary, die übrigen replica. Spalte IP:
--   Die Adresse der Primärinstanz ist die server_addr aus Aufgabe 1.
-- Variante Headlamp:
--   Custom Resources > postgresql.cnpg.io > Cluster > <cluster>,
--   im Status die Felder phase, currentPrimary, instances und readyInstances.
--   Die Adresse je Pod steht unter Workloads > Pods in der Spalte IP.
-- Erwartet: Phase "Cluster in healthy state", READY gleich INSTANCES.

-- Abschnitt 5 (Aufgabe 4): Einstellungen der Primärinstanz (RW-Server),
-- Vergleich mit der Tabelle "Stand der Kursumgebung" in AUFGABE.md
SELECT name, setting, unit, source
FROM pg_settings
WHERE name IN ('server_version', 'max_connections', 'shared_buffers',
               'work_mem', 'maintenance_work_mem', 'max_wal_size',
               'wal_level', 'synchronous_commit',
               'synchronous_standby_names', 'archive_mode', 'ssl',
               'io_method', 'default_transaction_isolation', 'TimeZone',
               'idle_in_transaction_session_timeout', 'statement_timeout')
ORDER BY name;

-- Aufgabe 5: Dienstwahl
-- Ticket übernehmen:            <cluster>-rw. Die Anwendung schreibt.
-- Eigenen Kommentar anzeigen:   <cluster>-rw. Nach dem Schreiben muss der
--                               Leser den eigenen Stand sehen; ein Replikat
--                               kann ihn noch nicht eingespielt haben.
-- Monatsbericht:                <cluster>-ro. Ein paar Sekunden Rückstand
--                               stören hier nicht, und die Primärinstanz hat
--                               weniger zu tun.
-- <cluster>-r verteilt auf alle Instanzen einschließlich der Primärinstanz;
-- welchen Stand eine Abfrage sieht, hängt von der je Verbindung gewählten
-- Instanz ab. Für keinen der drei Fälle ist das die passende Wahl.

-- Kontrolle
-- RO-Server
-- SELECT pg_is_in_recovery() AS is_replica,
--        (SELECT count(*) FROM tickets.read_test) AS row_count;
-- -- Ergebnis: is_replica t, row_count 1.
