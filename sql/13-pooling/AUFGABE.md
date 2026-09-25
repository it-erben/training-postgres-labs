# SQL-Übung 13: Verbindungen und Pooling

## Ziel

Jede Verbindung zum Ticketsystem belegt auf der Primärinstanz einen
eigenen Serverprozess. Du liest die Grenze `max_connections`, zählst deine
eigenen Verbindungen und ordnest sie über `application_name` zu. Mit
diesen Zahlen rechnest du nach, ob drei Instanzen einer Anwendung mit je
einem Npgsql-Pool in deinen Cluster passen. Danach erzeugst du eine offene
Transaktion ohne Aktivität, findest sie aus einer zweiten Verbindung und
lässt sie von einer Leerlaufgrenze beenden.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus und ändert keine
Daten. Arbeite auf dem RW-Server deiner Servergruppe mit `Auto commit` an
und `Auto rollback on error` aus.

Du brauchst zuerst ein Query Tool, für Aufgabe 2 drei weitere. Für
Aufgabe 4 und 5 verwendest du zwei davon als Verbindung A und B. Schließe
die übrigen Query Tools nach Aufgabe 3.

## Aufgaben

1. Lies die Grenzen deiner Instanz und zähle deine eigenen Verbindungen:

   ```sql
   SELECT name, setting
   FROM pg_settings
   WHERE name IN ('max_connections',
                  'superuser_reserved_connections',
                  'reserved_connections')
   ORDER BY name;

   SELECT current_setting('max_connections')::int AS max_connections,
          count(*) FILTER (WHERE usename = current_user) AS own_sessions
   FROM pg_stat_activity;
   ```

   Notiere `own_sessions`. `pg_stat_activity` hat eine Zeile je Serverprozess.
   `usename = current_user` wählt die Verbindungen deiner Rolle `app`,
   gleich aus welchem Werkzeug.

2. Öffne drei weitere Query Tools auf demselben Server. Führe in keinem
   davon etwas aus. Wiederhole im ersten Query Tool die Zählung aus
   Aufgabe 1 und schlüssele die Verbindungen dann auf:

   ```sql
   SELECT application_name, state, count(*) AS total
   FROM pg_stat_activity
   WHERE usename = current_user
   GROUP BY application_name, state
   ORDER BY application_name, state;
   ```

   Ordne jede Zeile einem Fenster im pgAdmin zu. Um wie viel ist `own_sessions`
   gestiegen?

3. Eine Anwendung läuft in drei Pods. Jeder Pod hat eine
   `NpgsqlDataSource` mit `Maximum Pool Size=20`. Berechne, ob die
   Anwendung in deinen Cluster passt:

   ```sql
   SELECT 3 * 20 AS app_demand,
          4 * 20 AS rolling_update_demand,
          current_setting('max_connections')::int
            - current_setting('superuser_reserved_connections')::int
            - current_setting('reserved_connections')::int AS slots_without_reserve,
          count(*) FILTER (WHERE usename IS NOT NULL
                             AND datname IS NOT NULL) AS already_used
   FROM pg_stat_activity;
   ```

   `already_used` zählt alle Sitzungen mit Rolle und Datenbank, auch
   fremde, denn die Plätze ohne Reserve teilen sich deine Werkzeuge, die
   Anwendung und die Sitzungen, die CloudNativePG selbst öffnet. WAL-Sender
   der Replikate haben keine Datenbank und belegen keinen Platz.

   Beantworte drei Fragen: Passen 60 Verbindungen neben den schon
   belegten? Passen sie während eines Rolling Updates, bei dem kurz ein
   vierter Pod läuft? Was passiert mit dem Npgsql-Standard
   `Maximum Pool Size=100`?

4. Erzeuge in Verbindung A eine offene Transaktion und lass sie offen:

   ```sql
   SET application_name = 'exercise13_a';
   BEGIN;
   SELECT count(*) AS open_tickets FROM tickets.ticket WHERE status = 'open';
   ```

   Suche sie in Verbindung B:

   ```sql
   SELECT application_name, state,
          now() - xact_start AS xact_age,
          now() - state_change AS idle_for
   FROM pg_stat_activity
   WHERE state = 'idle in transaction'
     AND usename = current_user;
   ```

   Referenzlauf mit zwei psql-Sitzungen (PostgreSQL 18.6), zwei Sekunden
   nach dem `SELECT` in A:

   ```text
    application_name |        state        |    xact_age     |    idle_for    
   ------------------+---------------------+-----------------+----------------
    exercise13_a     | idle in transaction | 00:00:02.002992 | 00:00:02.00152
   (1 row)
   ```

   Wiederhole die Abfrage in B nach einer halben Minute und vergleiche
   `xact_age`. Schließe die Transaktion danach in A mit `COMMIT;`.

5. Setze in Verbindung A eine Leerlaufgrenze und öffne wieder eine
   Transaktion:

   ```sql
   SET idle_in_transaction_session_timeout = '10s';
   BEGIN;
   SELECT count(*) AS open_tickets FROM tickets.ticket WHERE status = 'open';
   ```

   Warte mindestens 15 Sekunden, ohne in A etwas auszuführen. Frage dann
   in B:

   ```sql
   SELECT count(*) AS sessions_a,
          current_setting('idle_in_transaction_session_timeout') AS timeout_in_b
   FROM pg_stat_activity
   WHERE application_name = 'exercise13_a';
   ```

   Führe anschließend in A `SELECT 1;` aus und lies die Meldung.

## Ergebnis prüfen

Im ersten Query Tool nach Aufgabe 2, solange die drei weiteren Query Tools
offen sind:

```sql
SELECT current_setting('max_connections')::int AS max_connections,
       count(*) FILTER (WHERE usename = current_user) AS own_sessions
FROM pg_stat_activity;
```

Referenzlauf mit psql (PostgreSQL 18.6, lokal), vor Aufgabe 2 mit einer
Sitzung:

```text
 max_connections | own_sessions 
-----------------+--------------
             100 |            1
(1 row)
```

Derselbe Lauf mit einer Sitzung und drei weiteren offenen Sitzungen:

```text
 max_connections | own_sessions 
-----------------+--------------
             100 |            4
(1 row)
```

`own_sessions` muss nach Aufgabe 2 um mindestens drei höher sein als nach
Aufgabe 1. Im pgAdmin liegt der Wert meist höher als im Referenzlauf,
weil der Objektbaum eine eigene Verbindung hält. `max_connections` zeigt
den Wert deines Clusters.

Aufgabe 5 endete im Referenzlauf in B mit:

```text
 sessions_a | timeout_in_b 
------------+--------------
          0 | 0
(1 row)
```

und in A mit:

```text
FATAL:  25P03: terminating connection due to idle-in-transaction timeout
```

Ohne die Grenze war die Sitzung von A nach 13 Sekunden noch vorhanden
(`sessions_a` 1).

## Hinweise

Im pgAdmin meldet sich jedes Query Tool mit `application_name`
`pgAdmin 4 - CONN:` und einer Zahl, der Objektbaum mit
`pgAdmin 4 - DB:app`. Ist die Autovervollständigung beim Tippen
eingeschaltet, öffnet ein Query Tool eine zweite Verbindung. Das erklärt
einen Zuwachs um mehr als drei.

Für deine Anwendung stehen weniger Plätze bereit als `max_connections`.
`superuser_reserved_connections` hält die letzten Plätze für Superuser
frei, `reserved_connections` weitere für Rollen mit
`pg_use_reserved_connections`. Den Rest teilen sich alle Sitzungen mit
Rolle und Datenbank. Bei CloudNativePG gehören dazu der Instance Manager
des Operators (Rolle `postgres`, `application_name`
`cnpg-instance-manager`) und der Exporter für Grafana
(`cnpg_metrics_exporter`). Ist der Rest belegt, scheitert die nächste
Anmeldung von `app` mit `53300`
(`remaining connection slots are reserved for roles with the SUPERUSER attribute`).
Öffne in dieser Übung keine Verbindungen bis zur Grenze.

Die Rolle `app` sieht bei fremden Sitzungen Rolle, Datenbank und
`application_name`, aber keinen Zustand. Das Grafana-Dashboard
`CloudNativePG` zeigt alle Sitzungen deines Clusters je Pod: Zeile
`Server Health`, Spalte `Connections`, und in der Zeile
`Operational Stats` die Panels `Session States` und
`Longest Transaction`. Dort erscheint auch die offene Transaktion aus
Aufgabe 4, mit einigen Sekunden Verzögerung.

`25P03` kommt als `FATAL`. Der Server beendet die Verbindung und rollt die
offene Transaktion zurück. Für weitere Befehle in A braucht das
Query Tool eine neue Verbindung. Dort gilt die Grenze nicht mehr, weil
`SET` nur für die Sitzung gilt, in der es lief. Für eine
Anwendung gehört die Grenze an die Rolle, etwa
`ALTER ROLE app SET idle_in_transaction_session_timeout = '60s'`, oder
in die Cluster-Ressource.

Die .NET-Übung 01 im Ordner `uebungen/01-verbindungen` begrenzt einen
Npgsql-Pool auf vier Verbindungen und prüft die Grenze in
`pg_stat_activity`.

`loesung.sql` liest nur und lässt sich mehrfach ausführen. Aufgabe 4 und
5 stehen dort als Kommentarblöcke mit `-- Verbindung A` und
`-- Verbindung B`, weil eine Skriptausführung nur eine Verbindung hat.
