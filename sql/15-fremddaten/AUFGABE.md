# SQL-Übung 15: Fremddaten

## Ziel

Du richtest mit `postgres_fdw` einen Zugriff auf die eigene Datenbank ein,
als läge sie auf einem anderen Server. Vorher prüfst du, welche
Erweiterungen dein Cluster anbietet. Danach liest du im Ausführungsplan,
welcher Teil einer Abfrage auf der Gegenseite läuft und welcher lokal.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus. Jeder Codeblock
ist eine Ausführung, wie in Übung 0 beschrieben.

`postgres_fdw` ist keine vertrauenswürdige Erweiterung. Die Rolle `app`
kann sie nicht selbst anlegen:

```text
ERROR:  permission denied to create extension "postgres_fdw"
HINT:  Must be superuser to create this extension.
```

Der SQLSTATE dazu ist `42501`. Ein Superuser muss deshalb vorab einmal in
deiner Datenbank ausführen, beide Anweisungen als eine Ausführung:

```sql
CREATE EXTENSION IF NOT EXISTS postgres_fdw;
GRANT USAGE ON FOREIGN DATA WRAPPER postgres_fdw TO app;
```

Im Kurs übernimmt das der Betrieb. Ob beides erledigt ist, zeigt diese
Abfrage mit `t` in beiden Spalten:

```sql
SELECT EXISTS (SELECT FROM pg_extension WHERE extname = 'postgres_fdw') AS installed,
       EXISTS (SELECT FROM pg_foreign_data_wrapper
               WHERE fdwname = 'postgres_fdw'
                 AND has_foreign_data_wrapper_privilege(oid, 'USAGE')) AS usage_granted;
```

Die Gegenseite ist deine eigene Datenbank `app`, erreicht über den Dienst
`<cluster>-rw` deines Clusters. Du brauchst dazu das Passwort der Rolle
`app`. Trage es nur im Query Tool ein und speichere die Datei nicht.

## Aufgaben

1. Liste für `postgres_fdw`, `oracle_fdw`, `tds_fdw` und `btree_gist` auf,
   ob die Erweiterung im Image liegt (`pg_available_extensions`), ob sie
   in deiner Datenbank installiert ist, und lies für die Standardversion
   `trusted` aus `pg_available_extension_versions` ab. Eine Erweiterung, die
   im Image fehlt, soll trotzdem als Zeile erscheinen.
2. Lege den Server `course_loopback` mit `host '<cluster>-rw'`,
   `dbname 'app'`, `sslmode 'verify-full'` und
   `sslrootcert '/controller/certificates/server-ca.crt'` an. Der Hinweis
   zu `sslmode` erklärt den Pfad. Lege ein User Mapping
   für `CURRENT_USER` mit `user 'app'` und dem Passwort an. Lege das Schema
   `remote` an und importiere mit `IMPORT FOREIGN SCHEMA ... LIMIT TO` nur
   die Tabellen `agent` und `ticket` aus dem Schema `tickets`.
3. Zähle die offenen Tickets (`status = 'open'`) von Agents aus dem Team
   `Technical`: einmal über `tickets.ticket` und `tickets.agent`, einmal über
   `remote.ticket` und `remote.agent`. Gib beide Zahlen in einer Zeile aus.
4. Sieh dir die Zählung über `remote` mit `EXPLAIN (VERBOSE, COSTS OFF)` an.
   Welche Teile der Abfrage stehen in der Zeile `Remote SQL`, und was
   bleibt für den lokalen Server?
5. Nimm `SELECT count(*) FROM remote.ticket WHERE status = 'open'` und
   ergänze die Bedingung `created_at >= now() - interval '90 days'`.
   Vergleiche beide Pläne: Welche Bedingung steht im `Remote SQL`, welche
   in einer Zeile `Filter:`, und wo wird gezählt?

## Ergebnis prüfen

Beide Wege zählen dieselben Tickets:

```sql
SELECT (SELECT count(*) FROM tickets.ticket t JOIN tickets.agent a ON a.id = t.agent_id
        WHERE a.team = 'Technical' AND t.status = 'open') AS local,
       (SELECT count(*) FROM remote.ticket t JOIN remote.agent a ON a.id = t.agent_id
        WHERE a.team = 'Technical' AND t.status = 'open') AS remote;
```

```text
 local | remote
-------+--------
  3784 |   3784
(1 row)
```

Nach Übung 9 zählt deren Nachtrag mit, ein offenes Ticket von Agent 1 aus
`Technical`: Dann steht auf beiden Wegen 3785.

Filter und Zählung laufen auf der Gegenseite:

```sql
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*) FROM remote.ticket WHERE status = 'open';
```

Lokal bleibt ein einziger `Foreign Scan`. Referenzlauf auf dem Kurscluster
`trainer-pg`:

```text
                                 QUERY PLAN
-----------------------------------------------------------------------------
 Foreign Scan
   Output: (count(*))
   Relations: Aggregate on (remote.ticket)
   Remote SQL: SELECT count(*) FROM tickets.ticket WHERE ((status = 'open'))
 Query Identifier: 769354697196695057
(5 rows)
```

Die letzte Zeile erscheint, weil der Kurscluster `pg_stat_statements` lädt
und `compute_query_id` auf `auto` steht. Die Zahl unterscheidet sich von
Cluster zu Cluster. Ohne `pg_stat_statements` fehlt die Zeile.

Aufgabe 1 zeigte auf `trainer-pg` nach Übung 7:

```text
     name     | default_version | installed_version | trusted
--------------+-----------------+-------------------+---------
 btree_gist   | 1.8             | 1.8               | t
 oracle_fdw   | 1.2             |                   | f
 postgres_fdw | 1.2             | 1.2               | f
 tds_fdw      |                 |                   |
(4 rows)
```

Vor Übung 7 ist `installed_version` bei `btree_gist` leer. `oracle_fdw`
liegt im Image der Kursumgebung, `tds_fdw` fehlt. Die Spalte
`default_version` nennt die Version der SQL-Schnittstelle aus der
Steuerdatei der Erweiterung. Bei `oracle_fdw` ist 1.2 nicht die Version
der Software; die meldet erst `oracle_diag()` nach `CREATE EXTENSION`.

In Aufgabe 5 bleibt die Bedingung auf `created_at` lokal. Das `Remote SQL`
holt alle offenen Tickets, und ein lokaler `Aggregate`-Knoten zählt. Die
Ausgabe ist um die Spaltenliste des `Foreign Scan` gekürzt:

```text
 Aggregate
   Output: count(*)
   ->  Foreign Scan on remote.ticket
         ...
         Filter: (ticket.created_at >= (now() - '90 days'::interval))
         Remote SQL: SELECT created_at FROM tickets.ticket WHERE ((status = 'open'))
 Query Identifier: -4167830102801849831
(7 rows)
```

## Hinweise

`CREATE SERVER` und `CREATE USER MAPPING` bauen keine Verbindung auf. Ein
falscher Host oder ein falsches Passwort zeigt sich erst beim Import mit
`08001` und `could not connect to server "course_loopback"`. Die Zeile
`DETAIL` nennt die Ursache, etwa
`password authentication failed for user "app"`. Fehlt das Passwort im
User Mapping, meldet `postgres_fdw` `2F003`
`password or GSSAPI delegated credentials required`, weil sich eine Rolle
ohne Superuser an der Gegenseite mit Passwort anmelden muss.

Fehlt das `GRANT USAGE`, scheitert schon `CREATE SERVER` mit `42501` und
`permission denied for foreign-data wrapper postgres_fdw`.

`sslmode 'verify-full'` prüft das Zertifikat der Gegenseite und ihren
Namen. Die Verbindung baut der Datenbankserver auf, pgAdmin ist daran
nicht beteiligt. Das Wurzelzertifikat muss deshalb auf dem Datenbankserver
liegen. Ohne weitere Angabe sucht libpq es unter `~/.postgresql/root.crt`
im Heimatverzeichnis des Betriebssystembenutzers, unter dem der Server
läuft. Im CloudNativePG-Pod liegt die CA des Clusters unter
`/controller/certificates/server-ca.crt`; die Serveroption `sslrootcert`
nennt diesen Pfad. Hast du den Server ohne die Option angelegt, ergänze
sie nachträglich:

```sql
ALTER SERVER course_loopback
    OPTIONS (ADD sslrootcert '/controller/certificates/server-ca.crt');
```

Fehlt die Option, scheitert der erste Zugriff über `remote`, auf dem
Kurscluster mit:

```text
ERROR:  08001: could not connect to server "course_loopback"
```

Die Zeile `DETAIL` darunter nennt nach Adresse und Port den gesuchten Pfad:
`root certificate file "/var/lib/postgresql/.postgresql/root.crt" does not exist`.
Es folgt `Either provide the file, use the system's trusted roots with
sslrootcert=system, or change sslmode to disable server certificate
verification.` Ohne TLS auf der Gegenseite lautet die Meldung
`server does not support SSL, but SSL was required`.

`postgres_fdw` überträgt nur Bedingungen mit eingebauten Operatoren und
Funktionen, die `IMMUTABLE` sind. `now()` ist `STABLE` und bleibt deshalb
lokal. Dasselbe gilt für jede eigene Funktion, auch wenn sie als
`IMMUTABLE` deklariert ist. Mit einem festen Zeitpunkt wie
`timestamptz '2026-06-01 00:00+00'` statt `now() - interval '90 days'`
wandert die Bedingung wieder mit.

Das Passwort steht im Klartext im Katalog. `pg_user_mappings.umoptions`
zeigt es dir als Eigentümer des Mappings.

`loesung.sql` entfernt zu Beginn das Schema `remote` und den Server
`course_loopback` samt User Mapping und lässt sich deshalb mehrfach
ausführen. Die Datei enthält `<cluster>-rw` und `<passwort>` als
Platzhalter; ersetze beide nur im Query Tool.
