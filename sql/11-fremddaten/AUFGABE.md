# SQL-Übung 11: Fremddaten

## Ziel

Du richtest mit `postgres_fdw` einen Zugriff auf die eigene Datenbank ein,
als läge sie auf einem anderen Server. Du prüfst vorher, welche
Erweiterungen dein Cluster anbietet. Danach liest du im Ausführungsplan,
welcher Teil einer Abfrage auf der Gegenseite läuft und welcher lokal.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus.

`postgres_fdw` ist keine vertrauenswürdige Erweiterung. Die Rolle `app`
kann sie nicht selbst anlegen:

```text
ERROR:  permission denied to create extension "postgres_fdw"
HINT:  Must be superuser to create this extension.
```

Der SQLSTATE dazu ist `42501`. Ein Superuser muss deshalb vorab einmal in
deiner Datenbank ausführen:

```sql
CREATE EXTENSION IF NOT EXISTS postgres_fdw;
GRANT USAGE ON FOREIGN DATA WRAPPER postgres_fdw TO app;
```

Im Kurs übernimmt das der Betrieb. Ob beides erledigt ist, zeigt diese
Abfrage mit `t` in beiden Spalten:

```sql
SELECT EXISTS (SELECT FROM pg_extension WHERE extname = 'postgres_fdw') AS installiert,
       EXISTS (SELECT FROM pg_foreign_data_wrapper
               WHERE fdwname = 'postgres_fdw'
                 AND has_foreign_data_wrapper_privilege(oid, 'USAGE')) AS usage_recht;
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
2. Lege den Server `kurs_loopback` mit `host '<cluster>-rw'`,
   `dbname 'app'` und `sslmode 'verify-full'` an. Lege ein User Mapping
   für `CURRENT_USER` mit `user 'app'` und dem Passwort an. Lege das Schema
   `fern` an und importiere mit `IMPORT FOREIGN SCHEMA ... LIMIT TO` nur
   die Tabellen `agent` und `ticket` aus dem Schema `tickets`.
3. Zähle die offenen Tickets (`status = 'open'`) von Agents aus dem Team
   `Technik`: einmal über `tickets.ticket` und `tickets.agent`, einmal über
   `fern.ticket` und `fern.agent`. Gib beide Zahlen in einer Zeile aus.
4. Sieh dir die Zählung über `fern` mit `EXPLAIN (VERBOSE, COSTS OFF)` an.
   Welche Teile der Abfrage stehen in der Zeile `Remote SQL`, und was
   bleibt für den lokalen Server?
5. Nimm `SELECT count(*) FROM fern.ticket WHERE status = 'open'` und
   ergänze die Bedingung `created_at >= now() - interval '90 days'`.
   Vergleiche beide Pläne: Welche Bedingung steht im `Remote SQL`, welche
   in einer Zeile `Filter:`, und wo wird gezählt?

## Ergebnis prüfen

```sql
SELECT (SELECT count(*) FROM tickets.ticket t JOIN tickets.agent a ON a.id = t.agent_id
        WHERE a.team = 'Technik' AND t.status = 'open') AS lokal,
       (SELECT count(*) FROM fern.ticket t JOIN fern.agent a ON a.id = t.agent_id
        WHERE a.team = 'Technik' AND t.status = 'open') AS fern;
EXPLAIN (VERBOSE, COSTS OFF)
SELECT count(*) FROM fern.ticket WHERE status = 'open';
```

Die erste Abfrage zählt auf beiden Wegen dieselben Tickets:

```text
 lokal | fern
-------+------
  3784 | 3784
(1 row)
```

Im Plan der zweiten laufen Filter und Zählung auf der Gegenseite. Lokal
bleibt ein einziger `Foreign Scan`:

```text
                                 QUERY PLAN
-----------------------------------------------------------------------------
 Foreign Scan
   Output: (count(*))
   Relations: Aggregate on (fern.ticket)
   Remote SQL: SELECT count(*) FROM tickets.ticket WHERE ((status = 'open'))
(4 rows)
```

Aufgabe 1 liefert im lokalen Testlauf mit dem offiziellen Docker-Image
PostgreSQL 18.6 dieses Bild. Auf deinem Cluster kann es abweichen:

```text
     name     | default_version | installed_version | trusted
--------------+-----------------+-------------------+---------
 btree_gist   | 1.8             |                   | t
 oracle_fdw   |                 |                   |
 postgres_fdw | 1.2             | 1.2               | f
 tds_fdw      |                 |                   |
(4 rows)
```

In Aufgabe 5 bleibt die Bedingung auf `created_at` lokal. Das `Remote SQL`
holt alle offenen Tickets, und ein lokaler `Aggregate`-Knoten zählt. Die
Ausgabe ist um die Spaltenliste des `Foreign Scan` gekürzt:

```text
 Aggregate
   Output: count(*)
   ->  Foreign Scan on fern.ticket
         ...
         Filter: (ticket.created_at >= (now() - '90 days'::interval))
         Remote SQL: SELECT created_at FROM tickets.ticket WHERE ((status = 'open'))
(6 rows)
```

## Hinweise

`CREATE SERVER` und `CREATE USER MAPPING` bauen keine Verbindung auf. Ein
falscher Host oder ein falsches Passwort zeigt sich erst beim Import mit
`08001` und `could not connect to server "kurs_loopback"`. Die Zeile
`DETAIL` nennt die Ursache, etwa
`password authentication failed for user "app"`. Fehlt das Passwort im
User Mapping, meldet `postgres_fdw` `2F003`
`password or GSSAPI delegated credentials required`: Eine Rolle ohne
Superuser muss sich an der Gegenseite mit Passwort anmelden.

Fehlt das `GRANT USAGE`, scheitert schon `CREATE SERVER` mit `42501` und
`permission denied for foreign-data wrapper postgres_fdw`.

`sslmode 'verify-full'` prüft das Zertifikat der Gegenseite und ihren
Namen. Die Verbindung baut der Datenbankserver auf, nicht pgAdmin. Das
Wurzelzertifikat muss deshalb auf dem Datenbankserver liegen.

`postgres_fdw` überträgt nur Bedingungen mit eingebauten Operatoren und
Funktionen, die `IMMUTABLE` sind. `now()` ist `STABLE` und bleibt deshalb
lokal. Dasselbe gilt für jede eigene Funktion, auch wenn sie als
`IMMUTABLE` deklariert ist. Mit einem festen Zeitpunkt wie
`timestamptz '2026-06-01 00:00+00'` statt `now() - interval '90 days'`
wandert die Bedingung wieder mit.

Das Passwort steht im Klartext im Katalog. `pg_user_mappings.umoptions`
zeigt es dir als Eigentümer des Mappings.

`loesung.sql` entfernt zu Beginn das Schema `fern` und den Server
`kurs_loopback` samt User Mapping und lässt sich deshalb mehrfach
ausführen. Die Datei enthält `<cluster>-rw` und `<passwort>` als
Platzhalter; ersetze beide nur im Query Tool.
