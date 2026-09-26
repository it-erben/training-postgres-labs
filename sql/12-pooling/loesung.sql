-- Musterlösung zu SQL-Übung 12. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Der ausführbare Teil liest nur und lässt sich deshalb mehrfach
-- ausführen; er setzt zu Beginn die Leerlaufgrenze der eigenen Sitzung
-- zurück. Aufgabe 2 braucht zusätzliche Query Tools, Aufgabe 4 und 5 zwei
-- gleichzeitige Verbindungen. Die Schritte dafür stehen als Kommentar,
-- markiert mit -- Verbindung A und -- Verbindung B, weil eine
-- Skriptausführung nur eine Verbindung hat.

-- Abschnitt 1: Leerlaufgrenze der eigenen Sitzung zurücksetzen
RESET idle_in_transaction_session_timeout;

-- Abschnitt 2 (Aufgabe 1): Grenzen der Instanz
SELECT name, setting
FROM pg_settings
WHERE name IN ('max_connections',
               'superuser_reserved_connections',
               'reserved_connections')
ORDER BY name;

-- Abschnitt 3 (Aufgabe 1): eigene Verbindungen
SELECT current_setting('max_connections')::int AS max_connections,
       count(*) FILTER (WHERE usename = current_user) AS own_sessions
FROM pg_stat_activity;
-- Mit einem einzigen Query Tool ist own_sessions mindestens 1. Im pgAdmin kommt
-- meist die Verbindung des Objektbaums dazu.

-- Abschnitt 4 (Aufgabe 2): weitere Query Tools öffnen, dann im ersten
-- Query Tool
SELECT application_name, state, count(*) AS total
FROM pg_stat_activity
WHERE usename = current_user
GROUP BY application_name, state
ORDER BY application_name, state;
-- Jedes Query Tool erscheint als "pgAdmin 4 - CONN:<zahl>", der
-- Objektbaum als "pgAdmin 4 - DB:app". Die drei neuen Query Tools stehen
-- auf idle, das ausführende auf active. own_sessions aus Aufgabe 1 ist um drei
-- gestiegen.

-- Abschnitt 5 (Aufgabe 3): Rechnung
SELECT 3 * 20 AS app_demand,
       4 * 20 AS rolling_update_demand,
       current_setting('max_connections')::int
         - current_setting('superuser_reserved_connections')::int
         - current_setting('reserved_connections')::int AS slots_without_reserve,
       count(*) FILTER (WHERE usename IS NOT NULL
                          AND datname IS NOT NULL) AS already_used
FROM pg_stat_activity;
-- Mit max_connections 100 bleiben 97 Plätze ohne Reserve. Sie teilen sich
-- alle Sitzungen mit Rolle und Datenbank: pgAdmin, Migrationen, Jobs,
-- der Instance Manager des Operators (postgres, cnpg-instance-manager)
-- und der Exporter (cnpg_metrics_exporter). WAL-Sender der Replikate
-- (streaming_replica) haben keine Datenbank und belegen keinen Platz.
-- 60 passen, solange already_used höchstens 37 ist. Während eines Rolling
-- Updates läuft ein vierter Pod mit weiteren 20; dann darf already_used
-- höchstens 17 sein. Mit dem Npgsql-Standard Maximum Pool Size=100
-- bräuchten drei Pods bis zu 300 Verbindungen. Sobald die 97 Plätze
-- belegt sind, scheitert jede weitere Anmeldung von app mit 53300.

-- Aufgabe 4: idle in transaction erzeugen und finden
--
-- Verbindung A:
-- SET application_name = 'exercise12_a';
-- BEGIN;
-- SELECT count(*) AS open_tickets FROM tickets.ticket WHERE status = 'open';
-- -- 13144, nach Übung 9 mit deren Nachtrag 13145; die Transaktion
-- -- bleibt offen.
--
-- Verbindung B:
-- SELECT application_name, state,
--        now() - xact_start AS xact_age,
--        now() - state_change AS idle_for
-- FROM pg_stat_activity
-- WHERE state = 'idle in transaction'
--   AND usename = current_user;
-- -- Eine Zeile exercise12_a, state idle in transaction. xact_age
-- -- wächst bei jeder Wiederholung.
--
-- Verbindung A:
-- COMMIT;

-- Aufgabe 5: Leerlaufgrenze
--
-- Verbindung A:
-- SET idle_in_transaction_session_timeout = '10s';
-- BEGIN;
-- SELECT count(*) AS open_tickets FROM tickets.ticket WHERE status = 'open';
--
-- Verbindung B (mehr als 10 Sekunden später):
-- SELECT count(*) AS sessions_a,
--        current_setting('idle_in_transaction_session_timeout') AS timeout_in_b
-- FROM pg_stat_activity
-- WHERE application_name = 'exercise12_a';
-- -- sessions_a 0, timeout_in_b 0: Die Grenze galt nur in A.
--
-- Verbindung A:
-- SELECT 1;
-- -- FATAL: 25P03: terminating connection due to idle-in-transaction timeout
-- -- Die Verbindung ist beendet, die Transaktion zurückgerollt.

-- Abschnitt 6: Kontrolle
SELECT current_setting('max_connections')::int AS max_connections,
       count(*) FILTER (WHERE usename = current_user) AS own_sessions
FROM pg_stat_activity;
