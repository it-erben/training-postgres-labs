# SQL-Übung 8: Materialized Views

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
brauchst du zwei Verbindungen A und B.

Entferne zu Beginn die Übungsobjekte:

```sql
DROP MATERIALIZED VIEW IF EXISTS tickets.team_bericht;
DROP VIEW IF EXISTS tickets.team_aktuell;
DELETE FROM tickets.ticket WHERE metadata ? 'kurs_modul08';
```

## Aufgaben

1. Lege `tickets.team_aktuell` als View an: Tickets je Team und Monat.

   ```sql
   SET TimeZone = 'UTC';
   CREATE VIEW tickets.team_aktuell AS
   SELECT a.team, date_trunc('month', t.created_at)::date AS monat,
          count(*) AS tickets_erstellt
   FROM tickets.ticket t
   JOIN tickets.agent a ON a.id = t.agent_id
   GROUP BY a.team, date_trunc('month', t.created_at);
   ```

   Eine View speichert kein Ergebnis. Jede Abfrage auf `team_aktuell` liest
   den vollständigen Bestand von `ticket` und `agent` zum Zeitpunkt der
   Abfrage neu.

2. Lege `tickets.team_bericht` als Materialized View mit derselben Abfrage
   an, dazu einen eindeutigen Index auf `(team, monat)`:

   ```sql
   CREATE MATERIALIZED VIEW tickets.team_bericht AS
   SELECT a.team, date_trunc('month', t.created_at)::date AS monat,
          count(*) AS tickets_erstellt
   FROM tickets.ticket t
   JOIN tickets.agent a ON a.id = t.agent_id
   GROUP BY a.team, date_trunc('month', t.created_at);

   CREATE UNIQUE INDEX team_bericht_team_monat_idx
       ON tickets.team_bericht (team, monat);
   ```

   Eine Materialized View speichert das Ergebnis physisch wie eine Tabelle.
   Der eindeutige Index ist Voraussetzung für Aufgabe 4:
   `REFRESH MATERIALIZED VIEW CONCURRENTLY` verlangt mindestens einen
   Unique-Index über alle Zeilen der Materialized View.

3. Füge einen Nachtrag ein und zeige die Abweichung zwischen View und
   Materialized View:

   ```sql
   SELECT (SELECT sum(tickets_erstellt) FROM tickets.team_aktuell) AS view_summe,
          (SELECT sum(tickets_erstellt) FROM tickets.team_bericht) AS matview_summe;

   INSERT INTO tickets.ticket (agent_id, subject, status, priority, metadata, created_at)
   VALUES (1, 'Nachtrag fuer Teambericht', 'open', 1, '{"kurs_modul08": true}',
           tickets.seed_base_date());

   SELECT (SELECT sum(tickets_erstellt) FROM tickets.team_aktuell) AS view_summe,
          (SELECT sum(tickets_erstellt) FROM tickets.team_bericht) AS matview_summe;
   ```

   Referenzlauf:

   ```text
    view_summe | matview_summe 
   ------------+---------------
        720159 |        720159
   (1 row)

    view_summe | matview_summe 
   ------------+---------------
        720160 |        720159
   (1 row)
   ```

   `team_aktuell` zeigt den Nachtrag sofort, weil jede Abfrage die
   zugrunde liegenden Tabellen neu liest. `team_bericht` bleibt beim alten
   Stand, bis ein `REFRESH` läuft.

4. Aktualisiere `tickets.team_bericht` gleichzeitig zu einer laufenden
   Lesetransaktion. Setze zuerst in Verbindung B `lock_timeout` niedrig,
   damit ein gewöhnlicher `REFRESH` nicht endlos wartet.

   In A:

   ```sql
   SET application_name = 'uebung_a';
   BEGIN;
   SELECT count(*) FROM tickets.team_bericht;
   ```

   Die Transaktion bleibt offen. In B:

   ```sql
   SET application_name = 'uebung_b';
   SET lock_timeout = '1s';
   REFRESH MATERIALIZED VIEW tickets.team_bericht;
   ```

   Der gewöhnliche `REFRESH` nimmt eine `ACCESS EXCLUSIVE`-Sperre auf die
   Materialized View und wartet auf die von A gehaltene Lesesperre. Nach
   einer Sekunde bricht er mit SQLSTATE `55P03`
   (`canceling statement due to lock timeout`) ab. Führe stattdessen aus:

   ```sql
   REFRESH MATERIALIZED VIEW CONCURRENTLY tickets.team_bericht;
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

```sql
SELECT (SELECT sum(tickets_erstellt) FROM tickets.team_aktuell) AS view_summe,
       (SELECT sum(tickets_erstellt) FROM tickets.team_bericht) AS matview_summe;
SELECT matviewname, ispopulated FROM pg_matviews WHERE schemaname = 'tickets';
RESET TimeZone;
```

Referenzlauf nach dem `REFRESH ... CONCURRENTLY` aus Aufgabe 4:

```text
 view_summe | matview_summe 
------------+---------------
     720160 |        720160
(1 row)

 matviewname  | ispopulated 
--------------+-------------
 team_bericht | t
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

`ispopulated = f` zeigt eine Materialized View, die mit `WITH NO DATA`
angelegt und noch nie befüllt wurde. `REFRESH MATERIALIZED VIEW
CONCURRENTLY` verlangt vorhandene Daten und schlägt auf einer solchen
Materialized View fehl.

`loesung.sql` entfernt zu Beginn `tickets.team_bericht`,
`tickets.team_aktuell` und den markierten Nachtrag und lässt sich deshalb
mehrfach ausführen. Die Anweisungen der Verbindungen A und B aus Aufgabe 4
stehen darin als Kommentarblöcke, weil eine einzelne Skriptausführung keine
zweite Verbindung besitzt.
