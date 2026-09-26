# SQL-Übung 9: Materialized Views

## Ziel

Du legst denselben Teambericht einmal als View und einmal als Materialized
View an, zeigst die Abweichung nach einem Nachtrag und aktualisierst die
Materialized View, ohne lesende Sitzungen zu blockieren. Zum Schluss
begründest du die Wahl zwischen direkter Abfrage, View und Materialized
View für einen Bericht, der bis zu fünf Minuten alt sein darf.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus. Für Aufgabe 4
brauchst du zwei Verbindungen A und B. Jeder Codeblock ist eine Ausführung,
wie in Übung 0 beschrieben.

Entferne zu Beginn die Übungsobjekte. Der Block läuft als eine Ausführung:

```sql
DROP MATERIALIZED VIEW IF EXISTS tickets.team_report;
DROP VIEW IF EXISTS tickets.team_live;
DELETE FROM tickets.ticket WHERE metadata ? 'course_module08';
```

## Aufgaben

1. Lege `tickets.team_live` als View an: Tickets je Team und Monat.

   ```sql
   CREATE VIEW tickets.team_live AS
   SELECT a.team,
          date_trunc('month', t.created_at AT TIME ZONE 'UTC')::date AS month,
          count(*) AS tickets_created
   FROM tickets.ticket t
   JOIN tickets.agent a ON a.id = t.agent_id
   GROUP BY a.team, date_trunc('month', t.created_at AT TIME ZONE 'UTC');
   ```

   `AT TIME ZONE 'UTC'` legt die Monatsgrenzen unabhängig von der
   Sitzungszeitzone fest. Eine View speichert kein Ergebnis. Jede Abfrage
   auf `team_live` liest den vollständigen Bestand von `ticket` und `agent`
   zum Zeitpunkt der Abfrage neu.

2. Lege `tickets.team_report` als Materialized View mit derselben Abfrage
   an, dazu einen eindeutigen Index auf `(team, month)`. Der Block läuft als
   eine Ausführung:

   ```sql
   CREATE MATERIALIZED VIEW tickets.team_report AS
   SELECT a.team,
          date_trunc('month', t.created_at AT TIME ZONE 'UTC')::date AS month,
          count(*) AS tickets_created
   FROM tickets.ticket t
   JOIN tickets.agent a ON a.id = t.agent_id
   GROUP BY a.team, date_trunc('month', t.created_at AT TIME ZONE 'UTC');

   CREATE UNIQUE INDEX team_report_team_month_idx
       ON tickets.team_report (team, month);
   ```

   Eine Materialized View speichert das Ergebnis physisch wie eine Tabelle.
   Der eindeutige Index ist Voraussetzung für Aufgabe 4:
   `REFRESH MATERIALIZED VIEW CONCURRENTLY` verlangt mindestens einen
   Unique-Index über alle Zeilen der Materialized View.

3. Füge einen Nachtrag ein und zeige die Abweichung zwischen View und
   Materialized View. Zuerst die Summen vor dem Nachtrag:

   ```sql
   SELECT (SELECT sum(tickets_created) FROM tickets.team_live) AS view_total,
          (SELECT sum(tickets_created) FROM tickets.team_report) AS matview_total;
   ```

   ```text
    view_total | matview_total
   ------------+---------------
        720159 |        720159
   (1 row)
   ```

   Der Nachtrag:

   ```sql
   INSERT INTO tickets.ticket (agent_id, subject, status, priority, metadata, created_at)
   VALUES (1, 'Nachtrag fuer Teambericht', 'open', 1, '{"course_module08": true}',
           tickets.seed_base_date());
   ```

   Dieselbe Abfrage wie vor dem Nachtrag, als eigener Block:

   ```sql
   SELECT (SELECT sum(tickets_created) FROM tickets.team_live) AS view_total,
          (SELECT sum(tickets_created) FROM tickets.team_report) AS matview_total;
   ```

   ```text
    view_total | matview_total
   ------------+---------------
        720160 |        720159
   (1 row)
   ```

   `team_live` zeigt den Nachtrag sofort, weil jede Abfrage die
   zugrunde liegenden Tabellen neu liest. `team_report` bleibt beim alten
   Stand, bis ein `REFRESH` läuft.

4. Aktualisiere `tickets.team_report` gleichzeitig zu einer laufenden
   Lesetransaktion. Setze zuerst in Verbindung B `lock_timeout` niedrig,
   damit ein gewöhnlicher `REFRESH` nicht endlos wartet.

   In A:

   ```sql
   SET application_name = 'exercise_a';
   ```

   Danach in A, beide Anweisungen als eine Ausführung:

   ```sql
   BEGIN;
   SELECT count(*) FROM tickets.team_report;
   ```

   Die Transaktion bleibt offen. In B zuerst die beiden Einstellungen, als
   eine Ausführung:

   ```sql
   SET application_name = 'exercise_b';
   SET lock_timeout = '1s';
   ```

   Danach in B:

   ```sql
   REFRESH MATERIALIZED VIEW tickets.team_report;
   ```

   Stünden `SET` und `REFRESH` in einer Ausführung, liefen sie als eine
   Transaktion, und der Fehler nähme auch `SET lock_timeout` wieder zurück.

   Der gewöhnliche `REFRESH` nimmt eine `ACCESS EXCLUSIVE`-Sperre auf die
   Materialized View und wartet auf die von A gehaltene Lesesperre. Nach
   einer Sekunde bricht er mit SQLSTATE `55P03`
   (`canceling statement due to lock timeout`) ab. Führe stattdessen aus:

   ```sql
   REFRESH MATERIALIZED VIEW CONCURRENTLY tickets.team_report;
   ```

   Diese Anweisung läuft durch, obwohl A seine Transaktion weiterhin offen
   hält. `CONCURRENTLY` nimmt nur eine `EXCLUSIVE`-Sperre, die lesende
   Zugriffe zulässt. Auch mit `CONCURRENTLY` rechnet PostgreSQL die
   gesamte Abfrage neu. Das Ergebnis vergleicht es mit dem alten Bestand
   und ändert in der Materialized View nur die abweichenden Zeilen.
   Schließe danach A ab:

   ```sql
   COMMIT;
   ```

5. Begründe schriftlich die Wahl zwischen direkter Abfrage, View und
   Materialized View für einen Bericht, der bis zu fünf Minuten alt sein
   darf. Berücksichtige, wie oft der Bericht gelesen wird, wie teuer die
   zugrunde liegende Aggregation ist und wer den Aktualisierungszeitpunkt
   bestimmt.

## Ergebnis prüfen

Nach dem `REFRESH ... CONCURRENTLY` aus Aufgabe 4 stimmen beide Summen
überein:

```sql
SELECT (SELECT sum(tickets_created) FROM tickets.team_live) AS view_total,
       (SELECT sum(tickets_created) FROM tickets.team_report) AS matview_total;
```

```text
 view_total | matview_total
------------+---------------
     720160 |        720160
(1 row)
```

Die Materialized View ist befüllt:

```sql
SELECT matviewname, ispopulated FROM pg_matviews WHERE schemaname = 'tickets';
```

```text
 matviewname | ispopulated
-------------+-------------
 team_report | t
(1 row)
```

## Hinweise

Eine direkte Abfrage ohne View oder Materialized View liest bei jedem
Aufruf den vollständigen, aktuellen Bestand. Sie passt, wenn der Bericht
selten gelesen wird oder aktuelle Zahlen wichtiger sind als eine kurze
Antwortzeit.

Eine View gibt einer Abfrage einen Namen und spart keinen Rechenaufwand.
Jeder Aufruf führt die volle Aggregation erneut aus. Sie passt, wenn es
darum geht, dieselbe SQL-Formulierung an mehreren Stellen zu verwenden.
Die Datenbank entlastet sie nicht.

Eine Materialized View spart bei jedem Lesezugriff Rechenaufwand. Dafür
muss ausdrücklich festgelegt sein, wann und wie sie aktualisiert wird. Zu
einem Bericht, der fünf Minuten alt sein darf, passt ein periodischer
`REFRESH ... CONCURRENTLY` etwa alle vier Minuten, ausgelöst von einem
Scheduler außerhalb der Datenbank.

Die `EXCLUSIVE`-Sperre aus Aufgabe 4 lässt Leser durch, hält aber einen
zweiten `REFRESH` sowie `VACUUM` und `ANALYZE` auf derselben View auf.
Dauert ein Lauf länger als das Intervall, stellt sich der nächste hinter
ihm an. Der Job setzt deshalb in seiner Sitzung ein kurzes `lock_timeout`
wie Verbindung B in Aufgabe 4. Der wartende `REFRESH` gibt dann mit
`55P03` auf, und der nächste Lauf versucht es erneut. Ein
`statement_timeout` über der üblichen Laufzeit beendet einen hängenden
`REFRESH`. Ein gewöhnlicher `REFRESH`, der auf einen langen Leser wartet,
hält zusätzlich alle neuen Leser der View auf, weil sie sich hinter seiner
angeforderten `ACCESS EXCLUSIVE`-Sperre anstellen.

`ispopulated = f` zeigt eine Materialized View, die mit `WITH NO DATA`
angelegt und noch nie befüllt wurde. `REFRESH MATERIALIZED VIEW
CONCURRENTLY` verlangt vorhandene Daten und schlägt auf einer solchen
Materialized View fehl.

`loesung.sql` entfernt zu Beginn `tickets.team_report`,
`tickets.team_live` und den markierten Nachtrag und lässt sich deshalb
mehrfach ausführen. Die Anweisungen der Verbindungen A und B aus Aufgabe 4
stehen darin als Kommentarblöcke, weil eine einzelne Skriptausführung keine
zweite Verbindung besitzt.
