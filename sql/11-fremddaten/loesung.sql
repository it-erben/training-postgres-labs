-- Musterlösung zu SQL-Übung 11. Läuft vollständig im Query Tool und
-- lässt sich wiederholen: Die ersten Anweisungen entfernen Schema remote und
-- Server course_loopback samt User Mapping.
--
-- Vor dem Lauf zwei Platzhalter ersetzen, nur im Query Tool, ohne die
-- Datei zu speichern:
--   <cluster>-rw  Dienst des eigenen Clusters mit der Endung -rw
--   <passwort>    Passwort der Rolle app
DROP SCHEMA IF EXISTS remote CASCADE;
DROP SERVER IF EXISTS course_loopback CASCADE;

-- Aufgabe 1: Erweiterungen im Image, vertrauenswürdig, installiert
SELECT n.name,
       a.default_version,
       a.installed_version,
       v.trusted
FROM (VALUES ('postgres_fdw'), ('oracle_fdw'), ('tds_fdw'), ('btree_gist')) AS n(name)
LEFT JOIN pg_available_extensions AS a ON a.name = n.name
LEFT JOIN pg_available_extension_versions AS v
       ON v.name = a.name AND v.version = a.default_version
ORDER BY n.name;

-- Aufgabe 2: Server, User Mapping, Schema und Import
-- Die Gegenseite ist dieselbe Datenbank app über den Dienst <cluster>-rw.
-- verify-full braucht die CA auf dem Datenbankserver. Ohne weitere Angabe
-- sucht libpq ~/.postgresql/root.crt des Betriebssystembenutzers, unter
-- dem der Server läuft. Liegt die CA an einem anderen Pfad im
-- Datenbank-Pod, kommt sslrootcert '<ca-pfad>' in die OPTIONS. Fehlt die
-- Datei, meldet der erste Zugriff über remote 08001 mit root certificate
-- file "..." does not exist.
CREATE SERVER course_loopback
    FOREIGN DATA WRAPPER postgres_fdw
    OPTIONS (host '<cluster>-rw', dbname 'app', sslmode 'verify-full');
CREATE USER MAPPING FOR CURRENT_USER
    SERVER course_loopback
    OPTIONS (user 'app', password '<passwort>');
CREATE SCHEMA remote;
IMPORT FOREIGN SCHEMA tickets LIMIT TO (agent, ticket)
    FROM SERVER course_loopback INTO remote;

-- Aufgabe 3: Offene Tickets des Teams Technical lokal und über remote
SELECT (SELECT count(*) FROM tickets.ticket AS t
        JOIN tickets.agent AS a ON a.id = t.agent_id
        WHERE a.team = 'Technical' AND t.status = 'open') AS local,
       (SELECT count(*) FROM remote.ticket AS t
        JOIN remote.agent AS a ON a.id = t.agent_id
        WHERE a.team = 'Technical' AND t.status = 'open') AS remote;

-- Aufgabe 4: Join, Filter und Zählung laufen auf der Gegenseite
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*)
FROM remote.ticket AS t
JOIN remote.agent AS a ON a.id = t.agent_id
WHERE a.team = 'Technical' AND t.status = 'open';

-- Aufgabe 5: now() ist STABLE und wird nicht übertragen. Die Bedingung auf
-- created_at wertet PostgreSQL lokal aus, damit auch die Zählung.
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*) FROM remote.ticket WHERE status = 'open';
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*) FROM remote.ticket
WHERE status = 'open' AND created_at >= now() - interval '90 days';
