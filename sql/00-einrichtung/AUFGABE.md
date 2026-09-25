# SQL-Übung 0: Einrichtung

## Ziel

Du legst das Schema `tickets` in deiner eigenen Datenbank an und prüfst die
Verbindung im pgAdmin. Die Domäne ist ein Ticketsystem: Agents in vier
Teams bearbeiten Tickets mit Status, Priorität und JSONB-Metadaten, dazu
Kommentare als Baum.

## Ausgangsstand

Du hast eine eigene Servergruppe im pgAdmin mit einem RW-Server, Datenbank
`app`, Rolle `app`. Das Passwort steht im Secret `<cluster>-app` unter dem
Schlüssel `password`:

```bash
kubectl -n training-postgres get secret <cluster>-app \
  -o jsonpath='{.data.password}' | base64 -d
```

Ohne `kubectl` findest du das Secret in Headlamp unter `Configuration`,
`Secrets`, `<cluster>-app`.

## Aufgaben

1. Öffne das Query Tool auf dem RW-Server. Stelle `Auto commit` an und
   `Auto rollback on error` aus.
2. Prüfe die Verbindung:

   ```sql
   SELECT current_database(), current_user, version(), pg_is_in_recovery();
   ```

3. Öffne `setup.sql` aus dem Labs-Repository, kopiere den Inhalt ins Query
   Tool und führe ihn vollständig aus.
4. Lies die Kontrollabfrage am Ende der Ausgabe.
5. Suche die Tabellen im Object Explorer unter `app > Schemas > tickets`.
   Nach dem Lauf zeigt der Objektbaum das Schema erst nach `Refresh` im
   Kontextmenü.

## Ergebnis prüfen

Die Kontrollabfrage am Ende von `setup.sql` liefert:

```text
 agents | tickets | kommentare
--------+---------+------------
     50 |  800000 |    2000678
(1 row)
```

Die Spalten von `ticket`:

```sql
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = 'tickets' AND table_name = 'ticket'
ORDER BY ordinal_position;
```

```text
 column_name |        data_type         | is_nullable
-------------+--------------------------+-------------
 id          | bigint                   | NO
 agent_id    | bigint                   | YES
 subject     | text                     | NO
 status      | text                     | NO
 priority    | integer                  | NO
 metadata    | jsonb                    | NO
 created_at  | timestamp with time zone | NO
 closed_at   | timestamp with time zone | YES
(8 rows)
```

## Hinweise

Gegen eine lokale Testinstanz dauerte ein Lauf rund 58 Sekunden. Auf dem
Kurs-Server kann das anders aussehen. Ein erneuter Lauf von `setup.sql`
löscht das Schema `tickets` samt aller Übungsobjekte darin und baut es neu
auf. Mengen und Werte bleiben dabei gleich.

Bleibt der Lauf hängen und bricht nach fünf Sekunden mit
`ERROR: canceling statement due to lock timeout` ab, hat ein anderer
Query-Tool-Tab noch eine offene Transaktion auf einer Tabelle im Schema
`tickets`. Schließe diesen Tab oder führe dort `ROLLBACK;` aus und starte
`setup.sql` erneut.

pgAdmin zeigt nur das letzte Ergebnis einer Skriptausführung an. Die
Kontrollabfrage steht deshalb am Ende von `setup.sql`.
