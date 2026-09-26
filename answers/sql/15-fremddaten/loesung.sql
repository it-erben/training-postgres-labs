-- Musterlösung zu SQL-Übung 15. Im Query Tool abschnittsweise ausführen:
-- jeden mit "-- Abschnitt" beginnenden Block einzeln markieren und mit F5
-- ausführen. Abschnitt 1 entfernt Schema remote und Server course_loopback
-- samt User Mapping, die Datei lässt sich deshalb wiederholen.
--
-- Vor dem Lauf zwei Platzhalter ersetzen, nur im Query Tool, ohne die
-- Datei zu speichern:
--   <cluster>-rw  Dienst des eigenen Clusters mit der Endung -rw
--   <passwort>    Passwort der Rolle app

-- Abschnitt 1: Übungsobjekte entfernen
DROP SCHEMA IF EXISTS remote CASCADE;
DROP SERVER IF EXISTS course_loopback CASCADE;

-- Abschnitt 2 (Aufgabe 1): Erweiterungen im Image, vertrauenswürdig, installiert
SELECT n.name,
       a.default_version,
       a.installed_version,
       v.trusted
FROM (VALUES ('postgres_fdw'), ('oracle_fdw'), ('tds_fdw'), ('btree_gist')) AS n(name)
LEFT JOIN pg_available_extensions AS a ON a.name = n.name
LEFT JOIN pg_available_extension_versions AS v
       ON v.name = a.name AND v.version = a.default_version
ORDER BY n.name;

-- Abschnitt 3 (Aufgabe 2): Server, User Mapping, Schema und Import
-- Die Gegenseite ist dieselbe Datenbank app über den Dienst <cluster>-rw.
-- verify-full braucht die CA auf dem Datenbankserver. Ohne weitere Angabe
-- sucht libpq ~/.postgresql/root.crt des Betriebssystembenutzers, unter
-- dem der Server läuft. In einem CloudNativePG-Pod liegt die CA unter
-- /controller/certificates/server-ca.crt, deshalb steht sslrootcert in den
-- OPTIONS. Fehlt die Datei, meldet der erste Zugriff über remote 08001 mit
-- root certificate file "..." does not exist.
CREATE SERVER course_loopback
    FOREIGN DATA WRAPPER postgres_fdw
    OPTIONS (host '<cluster>-rw', dbname 'app', sslmode 'verify-full',
             sslrootcert '/controller/certificates/server-ca.crt');
CREATE USER MAPPING FOR CURRENT_USER
    SERVER course_loopback
    OPTIONS (user 'app', password '<passwort>');
CREATE SCHEMA remote;
IMPORT FOREIGN SCHEMA tickets LIMIT TO (agent, ticket)
    FROM SERVER course_loopback INTO remote;

-- Abschnitt 4 (Aufgabe 3): Offene Tickets des Teams Technical lokal und über remote
SELECT (SELECT count(*) FROM tickets.ticket AS t
        JOIN tickets.agent AS a ON a.id = t.agent_id
        WHERE a.team = 'Technical' AND t.status = 'open') AS local,
       (SELECT count(*) FROM remote.ticket AS t
        JOIN remote.agent AS a ON a.id = t.agent_id
        WHERE a.team = 'Technical' AND t.status = 'open') AS remote;

-- Abschnitt 5 (Aufgabe 4): Join, Filter und Zählung laufen auf der Gegenseite
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*)
FROM remote.ticket AS t
JOIN remote.agent AS a ON a.id = t.agent_id
WHERE a.team = 'Technical' AND t.status = 'open';

-- Abschnitt 6 (Aufgabe 5): Filter und Zählung auf der Gegenseite
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*) FROM remote.ticket WHERE status = 'open';

-- Abschnitt 7 (Aufgabe 5): now() ist STABLE und wird nicht übertragen. Die
-- Bedingung auf created_at wertet PostgreSQL lokal aus, damit auch die
-- Zählung.
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*) FROM remote.ticket
WHERE status = 'open' AND created_at >= now() - interval '90 days';
