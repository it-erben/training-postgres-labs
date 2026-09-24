-- Musterlösung zu SQL-Übung 11. Läuft vollständig im Query Tool und
-- lässt sich wiederholen: Die ersten Anweisungen entfernen Schema fern und
-- Server kurs_loopback samt User Mapping.
--
-- Vor dem Lauf zwei Platzhalter ersetzen, nur im Query Tool, ohne die
-- Datei zu speichern:
--   <cluster>-rw  Dienst des eigenen Clusters mit der Endung -rw
--   <passwort>    Passwort der Rolle app
DROP SCHEMA IF EXISTS fern CASCADE;
DROP SERVER IF EXISTS kurs_loopback CASCADE;

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
CREATE SERVER kurs_loopback
    FOREIGN DATA WRAPPER postgres_fdw
    OPTIONS (host '<cluster>-rw', dbname 'app', sslmode 'verify-full');
CREATE USER MAPPING FOR CURRENT_USER
    SERVER kurs_loopback
    OPTIONS (user 'app', password '<passwort>');
CREATE SCHEMA fern;
IMPORT FOREIGN SCHEMA tickets LIMIT TO (agent, ticket)
    FROM SERVER kurs_loopback INTO fern;

-- Aufgabe 3: Offene Technik-Tickets lokal und über fern
SELECT (SELECT count(*) FROM tickets.ticket AS t
        JOIN tickets.agent AS a ON a.id = t.agent_id
        WHERE a.team = 'Technik' AND t.status = 'open') AS lokal,
       (SELECT count(*) FROM fern.ticket AS t
        JOIN fern.agent AS a ON a.id = t.agent_id
        WHERE a.team = 'Technik' AND t.status = 'open') AS fern;

-- Aufgabe 4: Join, Filter und Zählung laufen auf der Gegenseite
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*)
FROM fern.ticket AS t
JOIN fern.agent AS a ON a.id = t.agent_id
WHERE a.team = 'Technik' AND t.status = 'open';

-- Aufgabe 5: now() ist STABLE und wird nicht übertragen. Die Bedingung auf
-- created_at wertet PostgreSQL lokal aus, damit auch die Zählung.
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*) FROM fern.ticket WHERE status = 'open';
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*) FROM fern.ticket
WHERE status = 'open' AND created_at >= now() - interval '90 days';
