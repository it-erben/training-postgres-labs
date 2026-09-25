-- Soundcheck der Kursumgebung: eine Zeile je Prüfung, status OK oder
-- PRÜFEN, detail mit dem gelesenen Wert. Im Query Tool des RW-Servers
-- als eine Ausführung starten (F5). Die Abfrage liest nur.
--
-- Die Zeilenzahlen stammen aus pg_class.reltuples. setup.sql endet mit
-- ANALYZE, das bei diesen Tabellengrößen jede Seite liest; die Werte sind
-- danach exakt, ohne dass die Abfrage 2,8 Millionen Zeilen zählt.
--
-- Vor setup.sql fehlt das Schema tickets. tickets.setup_run liest die
-- Abfrage deshalb über query_to_xml und nur, wenn die Tabelle existiert;
-- sonst meldet sie PRÜFEN, statt mit einem Fehler abzubrechen.
WITH session AS (
    SELECT current_user AS role_name,
           current_database() AS db_name,
           (SELECT rolsuper FROM pg_roles
            WHERE rolname = current_user) AS is_superuser,
           (SELECT pg_get_userbyid(datdba) FROM pg_database
            WHERE datname = current_database()) AS db_owner,
           current_setting('server_version_num')::int AS version_num,
           current_setting('server_version') AS version_text,
           pg_is_in_recovery() AS in_recovery,
           -- Ohne offene Transaktion beginnt die Transaktion mit dieser
           -- Anweisung, beide Zeitstempel sind dann gleich.
           now() = statement_timestamp() AS own_transaction,
           now() AS transaction_start
),
tls AS (
    SELECT ssl, version, cipher
    FROM pg_stat_ssl
    WHERE pid = pg_backend_pid()
),
fdw AS (
    SELECT (SELECT extversion FROM pg_extension
            WHERE extname = 'postgres_fdw') AS ext_version,
           CASE WHEN EXISTS (SELECT FROM pg_foreign_data_wrapper
                             WHERE fdwname = 'postgres_fdw')
                THEN has_foreign_data_wrapper_privilege('postgres_fdw', 'USAGE')
           END AS has_usage
),
setup AS (
    SELECT CASE WHEN to_regclass('tickets.setup_run') IS NOT NULL THEN
               query_to_xml($q$
                   SELECT round(extract(epoch FROM finished_at - started_at))
                              AS seconds,
                          pg_wal_lsn_diff(end_lsn, start_lsn) AS wal_bytes
                   FROM tickets.setup_run$q$, false, false, '')
           END AS x
),
data AS (
    SELECT (xpath('//seconds/text()', x))[1]::text::numeric AS seconds,
           (xpath('//wal_bytes/text()', x))[1]::text::numeric AS wal_bytes,
           -- reltuples ist -1, solange ANALYZE die Tabelle nicht kennt.
           (SELECT reltuples FROM pg_class
            WHERE oid = to_regclass('tickets.agent'))::bigint AS agents,
           (SELECT reltuples FROM pg_class
            WHERE oid = to_regclass('tickets.ticket'))::bigint AS tickets,
           (SELECT reltuples FROM pg_class
            WHERE oid = to_regclass('tickets.comment'))::bigint AS comments,
           CASE WHEN to_regnamespace('tickets') IS NOT NULL
                THEN has_schema_privilege('tickets', 'CREATE')
           END AS can_create
    FROM setup
)
SELECT c.check_name,
       CASE WHEN c.ok THEN 'OK' ELSE 'PRÜFEN' END AS status,
       c.detail
FROM session AS s
CROSS JOIN fdw AS f
CROSS JOIN data AS d
LEFT JOIN tls AS t ON true
CROSS JOIN LATERAL (VALUES
    (1, 'Rolle und Datenbank',
     s.role_name = 'app' AND s.db_name = 'app'
         AND NOT s.is_superuser AND s.db_owner = s.role_name,
     format('%s in %s, Eigentümer %s, %s', s.role_name, s.db_name, s.db_owner,
            CASE WHEN s.is_superuser THEN 'Superuser'
                 ELSE 'kein Superuser' END)),
    (2, 'PostgreSQL 18.6',
     s.version_num = 180006,
     s.version_text),
    (3, 'Primärinstanz',
     NOT s.in_recovery,
     CASE WHEN s.in_recovery THEN 'Replikat, pg_is_in_recovery() = true'
          ELSE 'pg_is_in_recovery() = false' END),
    (4, 'TLS',
     coalesce(t.ssl AND t.version = 'TLSv1.3', false),
     CASE WHEN t.ssl THEN t.version || ' ' || t.cipher
          ELSE 'unverschlüsselt' END),
    (5, 'Auto commit',
     s.own_transaction,
     CASE WHEN s.own_transaction THEN 'keine offene Transaktion'
          ELSE 'Transaktion offen seit '
               || to_char(s.transaction_start, 'HH24:MI:SS TZ')
     END),
    (6, 'Testdaten',
     d.agents = 50 AND d.tickets = 800000 AND d.comments = 2000678,
     CASE WHEN d.can_create IS NULL THEN 'Schema tickets fehlt'
          WHEN least(d.agents, d.tickets, d.comments) < 0 THEN 'kein ANALYZE'
          ELSE concat_ws(' | ', d.agents, d.tickets, d.comments) END),
    (7, 'Anlegen in tickets',
     coalesce(d.can_create, false),
     CASE WHEN d.can_create THEN 'CREATE auf Schema tickets'
          WHEN d.can_create IS NULL THEN 'Schema tickets fehlt'
          ELSE 'kein CREATE auf Schema tickets' END),
    (8, 'postgres_fdw',
     f.ext_version IS NOT NULL AND coalesce(f.has_usage, false),
     CASE WHEN f.ext_version IS NULL THEN 'Extension fehlt'
          WHEN f.has_usage
              THEN 'Version ' || f.ext_version || ', USAGE vorhanden'
          ELSE 'Version ' || f.ext_version || ', kein USAGE' END),
    (9, 'Laufzeit setup.sql',
     d.seconds IS NOT NULL,
     coalesce(d.seconds || ' s', 'keine Angabe in tickets.setup_run')),
    (10, 'WAL von setup.sql',
     d.wal_bytes IS NOT NULL,
     coalesce(pg_size_pretty(d.wal_bytes),
              'keine Angabe in tickets.setup_run')),
    (11, 'Größe der Datenbank',
     true,
     pg_size_pretty(pg_database_size(current_database())))
) AS c(pos, check_name, ok, detail)
ORDER BY c.pos;
