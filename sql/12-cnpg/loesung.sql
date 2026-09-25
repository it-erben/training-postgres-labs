-- Musterlösung zu SQL-Übung 12. Der ausführbare Teil läuft im Query Tool
-- des RW-Servers und lässt sich wiederholen: Er entfernt zu Beginn
-- tickets.lesetest und legt die Tabelle neu an. Anweisungen für den
-- RO-Server stehen als Kommentarblock, markiert mit -- RO-Server, weil eine
-- Skriptausführung nur eine Verbindung besitzt. Aufgabe 4 und 5 sind
-- Beobachtung und Begründung; ihre Lösung steht als Kommentar am Ende.
DROP TABLE IF EXISTS tickets.lesetest;

-- Aufgabe 1: Primärinstanz und Replikat unterscheiden
-- RW-Server
SELECT pg_is_in_recovery() AS replikat,
       inet_server_addr() AS server_adresse,
       current_setting('transaction_read_only') AS nur_lesen;
-- Ergebnis: replikat f, nur_lesen off.

-- RO-Server
-- SELECT pg_is_in_recovery() AS replikat,
--        inet_server_addr() AS server_adresse,
--        current_setting('transaction_read_only') AS nur_lesen;
-- -- Ergebnis: replikat t, nur_lesen on, eine andere server_adresse.

-- Aufgabe 2: Verschlüsselung der eigenen Verbindung
-- RW-Server
SELECT ssl, version, cipher, bits
FROM pg_stat_ssl
WHERE pid = pg_backend_pid();
-- Ergebnis: ssl t. Dieselbe Abfrage auf dem RO-Server zeigt die
-- Verbindung zum Replikat.

-- Aufgabe 3: Schreiben auf RW, sofort lesen auf RO
-- RW-Server
CREATE TABLE tickets.lesetest (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    notiz text NOT NULL,
    geschrieben_um timestamptz NOT NULL DEFAULT now()
);
INSERT INTO tickets.lesetest (notiz) VALUES ('geschrieben auf RW');
SELECT id, notiz, geschrieben_um, pg_current_wal_lsn() AS wal_position_rw
FROM tickets.lesetest;

-- RO-Server, direkt danach
-- SELECT id, notiz, geschrieben_um FROM tickets.lesetest;
-- SELECT pg_last_wal_replay_lsn() AS eingespielt_bis,
--        pg_last_xact_replay_timestamp() AS letzte_transaktion;
-- -- letzte_transaktion ist der Commit-Zeitpunkt auf der Primärinstanz,
-- -- wenige Millisekunden nach geschrieben_um. Die Zeile ist sichtbar,
-- -- sobald eingespielt_bis die wal_position_rw erreicht hat.
--
-- RO-Server, Schreibversuch
-- INSERT INTO tickets.lesetest (notiz) VALUES ('geschrieben auf RO');
-- -- ERROR: 25006: cannot execute INSERT in a read-only transaction

-- Aufgabe 4: Cluster-Status
-- Variante kubectl:
--   kubectl -n training-postgres get cluster <cluster>
--   Spalten STATUS (Phase), PRIMARY (Primärinstanz), INSTANCES und READY.
--   kubectl -n training-postgres get pods -l cnpg.io/cluster=<cluster> -L role -o wide
--   Spalte ROLE: eine Instanz primary, die übrigen replica. Spalte IP:
--   Die Adresse der Primärinstanz ist die server_adresse aus Aufgabe 1.
-- Variante Headlamp:
--   Custom Resources > postgresql.cnpg.io > Cluster > <cluster>,
--   im Status die Felder phase, currentPrimary, instances und readyInstances.
--   Die Adresse je Pod steht unter Workloads > Pods in der Spalte IP.
-- Erwartet: Phase "Cluster in healthy state", READY gleich INSTANCES.

-- Aufgabe 5: Dienstwahl
-- Ticket übernehmen:            <cluster>-rw. Die Anwendung schreibt.
-- Eigenen Kommentar anzeigen:   <cluster>-rw. Nach dem Schreiben muss der
--                               Leser den eigenen Stand sehen; ein Replikat
--                               kann ihn noch nicht eingespielt haben.
-- Monatsbericht:                <cluster>-ro. Ein Stand von einigen Sekunden
--                               stört nicht, die Primärinstanz wird entlastet.
-- <cluster>-r verteilt auf alle Instanzen einschließlich der Primärinstanz;
-- welchen Stand eine Abfrage sieht, hängt von der je Verbindung gewählten
-- Instanz ab. Für keinen der drei Fälle ist das die passende Wahl.

-- Kontrolle
-- RO-Server
-- SELECT pg_is_in_recovery() AS replikat,
--        (SELECT count(*) FROM tickets.lesetest) AS zeilen;
-- -- Ergebnis: replikat t, zeilen 1.
