-- Legt das Schema tickets mit dem Datenbestand der SQL-Übungen an.
-- Einmal vollständig im Query Tool ausführen (F5). Ein erneuter Lauf
-- löscht das Schema tickets samt aller Übungsobjekte darin und baut es neu auf.
-- Dauer: auf dem Kurscluster drei bis fünf Minuten. Andere Schemas
-- bleiben unberührt.
--
-- Das alte Schema verschwindet in einer eigenen Transaktion. So gibt ein
-- erneuter Lauf dessen Platz frei, bevor der neue Bestand entsteht.
--
-- Zeitpunkt und WAL-Position zu Beginn liegen bis zum Ende des Laufs in
-- der Sitzung; die Tabelle setup_run entsteht erst mit dem neuen Schema.
BEGIN;
SELECT set_config('setup.started_at', clock_timestamp()::text, false),
       set_config('setup.start_lsn', pg_current_wal_lsn()::text, false);
SET LOCAL lock_timeout = '5s';
DROP SCHEMA IF EXISTS tickets CASCADE;
COMMIT;

BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL search_path = tickets;
CREATE SCHEMA tickets;

CREATE FUNCTION seed_rand(i bigint, stream text) RETURNS double precision
    LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
    SELECT ('x' || substr(md5(stream || ':' || i::text), 1, 8))::bit(32)::bigint
           / 4294967296.0
$$;

CREATE FUNCTION seed_base_date() RETURNS timestamptz
    LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
    SELECT '2026-08-21 00:00:00+00'::timestamptz
$$;

CREATE TABLE agent (
    id bigint generated always as identity,
    name text not null,
    email text not null,
    team text not null
);

CREATE TABLE ticket (
    id bigint generated always as identity,
    agent_id bigint,
    subject text not null,
    status text not null,
    priority int not null,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null,
    closed_at timestamptz
);

CREATE TABLE comment (
    id bigint generated always as identity,
    ticket_id bigint not null,
    parent_id bigint,
    author text not null,
    body text not null,
    created_at timestamptz not null
);

INSERT INTO agent (name, email, team)
SELECT
    (ARRAY['Anna','Ben','Clara','David','Elif','Finn','Greta','Hakan',
           'Ines','Jonas'])[1 + floor(seed_rand(s.i, 'agent_first_name') * 10)::int]
        || ' '
        || (ARRAY['Bauer','Fischer','Hoffmann','Klein','Lindner','Meyer',
                   'Neumann','Schulz','Vogel','Wagner'])[1 + floor(seed_rand(s.i, 'agent_last_name') * 10)::int],
    'agent' || s.i || '@example.test',
    (ARRAY['Billing','Technical','Onboarding','Retention'])[1 + floor(seed_rand(s.i, 'agent_team') * 4)::int]
FROM generate_series(1, 50) AS s(i)
ORDER BY s.i;

-- status ist zu ~95 % 'closed', damit der partielle Index in Modul 02
-- sichtbar wirkt. ticket_rows berechnet status und created_at einmal, für
-- closed_at greift das INSERT über den Spaltennamen darauf zu. seed_rand
-- liefert mit denselben Argumenten ohnehin denselben Wert, der Alias spart
-- nur die zweite, lange CASE-Expression.
--
-- ticket.metadata: channel ist immer gesetzt (drei Werte). pool.attrs wählt
-- per seed_rand zwei der vier Schlüssel source, csat_score, reopened_count
-- und sla_target_hours und fasst sie zu einem Objekt zusammen. Jedes Ticket
-- trägt also nur zwei dieser vier Schlüssel. Selten kommen escalated und
-- vip_customer dazu, gesetzt nur unterhalb eines festen seed_rand-Schwellwerts
-- (0.12 bzw. 0.18). Darauf baut die GIN-Übung in Modul 02 auf.
WITH ticket_rows AS (
    SELECT
        t.id,
        CASE WHEN seed_rand(t.id, 'ticket_agent_assigned') < 0.9
             THEN (1 + floor(seed_rand(t.id, 'ticket_agent_pick') * 50))::bigint
             ELSE NULL
        END AS agent_id,
        (ARRAY['Login schlägt fehl','Rechnung unklar','Lieferung verspätet',
               'Feature-Anfrage','Zugriff verweigert','Preisanfrage',
               'Fehlermeldung beim Checkout','Vertrag kündigen'])[1 + floor(seed_rand(t.id, 'ticket_topic') * 8)::int] AS topic,
        CASE WHEN seed_rand(t.id, 'ticket_status_closed') < 0.95 THEN 'closed'
             ELSE (ARRAY['open','in_progress','waiting'])[1 + floor(seed_rand(t.id, 'ticket_status_other') * 3)::int]
        END AS status,
        (1 + floor(seed_rand(t.id, 'ticket_priority') * 4))::int AS priority,
        jsonb_build_object('channel', (ARRAY['email','phone','chat'])[1 + floor(seed_rand(t.id, 'ticket_channel') * 3)::int])
            || pool.attrs
            || CASE WHEN seed_rand(t.id, 'ticket_meta_escalated') < 0.12 THEN jsonb_build_object('escalated', true) ELSE '{}'::jsonb END
            || CASE WHEN seed_rand(t.id, 'ticket_meta_vip') < 0.18 THEN jsonb_build_object('vip_customer', true) ELSE '{}'::jsonb END AS metadata,
        seed_base_date() - (seed_rand(t.id, 'ticket_created_offset') * interval '730 days') AS created_at,
        interval '1 hour' + seed_rand(t.id, 'ticket_close_after') * interval '30 days' AS close_after
    FROM generate_series(1, (800000)::bigint) AS t(id)
    CROSS JOIN LATERAL (
        SELECT jsonb_object_agg(k.key, k.val) AS attrs
        FROM (
            SELECT key,
                CASE key
                    WHEN 'source' THEN to_jsonb((ARRAY['web','app','api'])[1 + floor(seed_rand(t.id, 'ticket_meta_source') * 3)::int])
                    WHEN 'csat_score' THEN to_jsonb((1 + floor(seed_rand(t.id, 'ticket_meta_csat') * 5))::int)
                    WHEN 'reopened_count' THEN to_jsonb(floor(seed_rand(t.id, 'ticket_meta_reopened') * 4)::int)
                    WHEN 'sla_target_hours' THEN to_jsonb((ARRAY[4,8,24,72])[1 + floor(seed_rand(t.id, 'ticket_meta_sla') * 4)::int])
                END AS val
            FROM unnest(ARRAY['source','csat_score','reopened_count','sla_target_hours']) AS k(key)
            ORDER BY seed_rand(t.id, 'ticket_meta_rank:' || key)
            LIMIT 2
        ) k
    ) pool
)
INSERT INTO ticket (agent_id, subject, status, priority, metadata, created_at, closed_at)
SELECT
    agent_id,
    'Ticket #' || id || ': ' || topic,
    status,
    priority,
    metadata,
    created_at,
    CASE WHEN status = 'closed' THEN LEAST(created_at + close_after, seed_base_date()) ELSE NULL END
FROM ticket_rows
ORDER BY id;

-- Schlüssel und Fremdschlüssel entstehen erst nach dem Laden. Ein Index,
-- der in einem Schritt gebaut wird, und eine Fremdschlüsselprüfung als eine
-- Abfrage schreiben deutlich weniger WAL als Einträge und Zeilensperren je Zeile.
ALTER TABLE agent ADD PRIMARY KEY (id), ADD UNIQUE (email);
ALTER TABLE ticket ADD PRIMARY KEY (id), ADD FOREIGN KEY (agent_id) REFERENCES agent (id);

-- comment.parent_id bildet echte Baumstrukturen für die rekursive CTE in
-- Modul 05. Jeder Kommentar außer dem ersten je Ticket wählt zufällig einen
-- der schon erzeugten Kommentare desselben Tickets als Elternteil. Das kann
-- die Wurzel sein, muss es aber nicht.
--
-- local_rn läuft über ein konstantes generate_series(1,4). Wie viele
-- Kommentare ein Ticket bekommt (Erwartungswert 2,5), entscheidet der
-- WHERE-Filter: lokale Position 1 immer, 2-4 mit sinkender
-- Wahrscheinlichkeit. Die übrigen local_rn-Werte je Ticket können deshalb
-- Lücken haben, z. B. {1,3,4}, wenn Position 2 verworfen wurde. survivor_rn
-- nummeriert die TATSÄCHLICH erzeugten Kommentare je Ticket lückenlos
-- durch. Der Elternteil kommt aus survivor_rn 1..(eigener survivor_rn - 1).
-- Eine Auswahl aus local_rn hätte auf eine verworfene Position fallen
-- können, und parent_id wäre dann NULL geblieben, obwohl der Kommentar
-- nicht der erste des Tickets ist.
--
-- created_at entsteht rekursiv: Die Wurzel liegt zwischen dem created_at
-- ihres Tickets und dem Basisdatum, jede weitere Ebene zwischen dem
-- created_at ihres Elternteils und dem Basisdatum. So liegt kein Kommentar
-- vor seinem Ticket oder vor seinem eigenen Elternteil.
--
-- Die Kommentare entstehen in Blöcken zu 100000 Tickets. Sortierungen und
-- Zwischenergebnisse jedes Blocks passen in kleinere temporäre Dateien, die
-- am Ende jeder Anweisung wieder frei werden. inserted führt die laufende
-- Nummer über die Blöcke fort, damit parent_id dieselben Werte trägt.
DO $$
DECLARE
    chunk_size constant bigint := 100000;
    first_ticket bigint;
    inserted bigint := 0;
    n bigint;
BEGIN
    FOR first_ticket IN SELECT generate_series(1, 800000, chunk_size) LOOP
        WITH RECURSIVE raw_shape AS (
            SELECT t.id AS ticket_id, local_rn
            FROM generate_series(first_ticket, first_ticket + chunk_size - 1) AS t(id)
            CROSS JOIN LATERAL generate_series(1, 4) AS local_rn
            WHERE local_rn = 1
               OR seed_rand(t.id * 10 + local_rn, 'comment_keep') < (CASE local_rn WHEN 2 THEN 0.65 WHEN 3 THEN 0.5 WHEN 4 THEN 0.35 END)
        ),
        shaped AS (
            SELECT
                ticket_id,
                local_rn,
                row_number() OVER (PARTITION BY ticket_id ORDER BY local_rn) AS survivor_rn,
                inserted + row_number() OVER (ORDER BY ticket_id, local_rn) AS global_rn
            FROM raw_shape
        ),
        withparent AS (
            SELECT
                ticket_id, local_rn, survivor_rn, global_rn,
                CASE WHEN survivor_rn = 1 THEN NULL
                     ELSE 1 + floor(seed_rand(ticket_id * 10 + survivor_rn, 'comment_parent_pick') * (survivor_rn - 1))::int
                END AS parent_survivor_rn
            FROM shaped
        ),
        comment_tree AS (
            SELECT
                w.ticket_id, w.local_rn, w.survivor_rn, w.global_rn, w.parent_survivor_rn,
                LEAST(
                    tk.created_at + interval '10 minutes' + seed_rand(w.ticket_id * 10 + w.local_rn, 'comment_created_offset') * interval '5 days',
                    seed_base_date()
                ) AS created_at
            FROM withparent w
            JOIN ticket tk ON tk.id = w.ticket_id
            WHERE w.parent_survivor_rn IS NULL

            UNION ALL

            SELECT
                w.ticket_id, w.local_rn, w.survivor_rn, w.global_rn, w.parent_survivor_rn,
                LEAST(
                    ct.created_at + interval '10 minutes' + seed_rand(w.ticket_id * 10 + w.local_rn, 'comment_created_offset') * interval '5 days',
                    seed_base_date()
                ) AS created_at
            FROM withparent w
            JOIN comment_tree ct ON ct.ticket_id = w.ticket_id AND ct.survivor_rn = w.parent_survivor_rn
        )
        INSERT INTO comment (ticket_id, parent_id, author, body, created_at)
        SELECT
            ct.ticket_id,
            parent.global_rn,
            (ARRAY['Kunde','Support'])[1 + floor(seed_rand(ct.ticket_id * 10 + ct.local_rn, 'comment_role') * 2)::int] || ' #' || ct.ticket_id,
            (ARRAY['Danke für die Rückmeldung.','Können Sie das genauer beschreiben?',
                   'Ich habe das Problem reproduziert.','Wird an das Fachteam weitergegeben.',
                   'Ist das jetzt gelöst?','Bitte prüfen Sie die angehängten Logs.',
                   'Ich kümmere mich morgen darum.','Vielen Dank für Ihre Geduld.'])[1 + floor(seed_rand(ct.ticket_id * 10 + ct.local_rn, 'comment_body') * 8)::int],
            ct.created_at
        FROM comment_tree ct
        LEFT JOIN comment_tree parent
            ON parent.ticket_id = ct.ticket_id AND parent.survivor_rn = ct.parent_survivor_rn
        ORDER BY ct.global_rn;
        GET DIAGNOSTICS n = ROW_COUNT;
        inserted := inserted + n;
    END LOOP;
END
$$;

ALTER TABLE comment ADD PRIMARY KEY (id),
    ADD FOREIGN KEY (ticket_id) REFERENCES ticket (id),
    ADD FOREIGN KEY (parent_id) REFERENCES comment (id);

ANALYZE agent;
ANALYZE ticket;
ANALYZE comment;

-- Start und Ende dieses Laufs für soundcheck.sql. end_lsn fällt vor das
-- COMMIT und umfasst damit den gesamten Aufbau.
CREATE TABLE setup_run (
    started_at timestamptz not null,
    finished_at timestamptz not null,
    start_lsn pg_lsn not null,
    end_lsn pg_lsn not null
);
INSERT INTO setup_run (started_at, finished_at, start_lsn, end_lsn)
SELECT current_setting('setup.started_at')::timestamptz, clock_timestamp(),
       current_setting('setup.start_lsn')::pg_lsn, pg_current_wal_lsn();
ANALYZE setup_run;
COMMIT;

-- Kontrolle: 50 | 800000 | 2000678
SELECT (SELECT count(*) FROM tickets.agent)   AS agents,
       (SELECT count(*) FROM tickets.ticket)  AS tickets,
       (SELECT count(*) FROM tickets.comment) AS comments;
