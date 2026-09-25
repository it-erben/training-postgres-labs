# SQL-Übung 10: Datentypen

## Ziel

Du überführst sechs Tickets aus einer Altbestandstabelle mit
Oracle-typischen Spaltentypen in eine Tabelle mit PostgreSQL-Typen. Aus
dem Zahlenflag wird `boolean`, aus der UTC-Wandzeit `timestamptz` und aus
der Gleitkommazahl `numeric`; den Schlüssel liefert eine Identity-Spalte.
Danach zeigst du, warum ein bloßer Cast von `timestamp` nach
`timestamptz` von der Sitzungszeitzone abhängt.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus. Jeder Codeblock
ist eine Ausführung, wie in Übung 0 beschrieben.

Lege die Altbestandstabelle `tickets.legacy_ticket` an. `closed` ist 1 für
ein geschlossenes und 0 für ein offenes Ticket. `created_at` enthält die
Uhrzeit in UTC, ohne Zonenangabe. Ticket 104 liegt um 02:30 Uhr UTC am
30.03.2025; in Berlin begann an diesem Tag um 02:00 Uhr Ortszeit die
Sommerzeit.

```sql
DROP TABLE IF EXISTS tickets.legacy_ticket;
CREATE TABLE tickets.legacy_ticket (
    id bigint,
    subject text,
    closed smallint,
    created_at timestamp,
    amount double precision
);
INSERT INTO tickets.legacy_ticket VALUES
    (101, 'Login schlägt fehl',    0, '2026-08-21 12:00:00',   19.99),
    (102, 'Rechnung doppelt',      1, '2025-12-31 23:30:00',    0.1),
    (103, 'Export bricht ab',      1, '2025-03-30 00:30:00',    0.2),
    (104, 'Passwort zurücksetzen', 0, '2025-03-30 02:30:00',    0.3),
    (105, 'Kontoauszug fehlt',     1, '2026-06-30 22:15:00', 1250),
    (106, 'Adresse ändern',        0, '2026-01-15 08:00:00',    4.6);
```

Der Block läuft als eine Ausführung. Er lässt sich jederzeit erneut
ausführen und stellt den Ausgangsstand wieder her.

## Aufgaben

1. Lege `tickets.ticket_new` mit diesen Spalten an:

   | Spalte       | Typ und Regel                                     |
   | ------------ | ------------------------------------------------- |
   | `id`         | `bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY` |
   | `legacy_id`  | `bigint UNIQUE NOT NULL`, die ursprüngliche ID    |
   | `subject`    | `text NOT NULL`                                   |
   | `is_closed`  | `boolean NOT NULL`                                |
   | `created_at` | `timestamptz NOT NULL`                            |
   | `amount`     | `numeric(12,2) NOT NULL`                          |

2. Übernimm alle sechs Zeilen mit einem einzigen `INSERT ... SELECT`.
   `id` erzeugt die Identity, `legacy_id` erhält die alte ID. Wandle
   `closed` mit `closed = 1` in einen Wahrheitswert, `created_at` mit
   `created_at AT TIME ZONE 'UTC'` in einen Zeitpunkt und `amount` mit
   `round(amount::numeric, 2)` in einen Dezimalwert.
3. Zeige, dass ein bloßer Cast `created_at::timestamptz` von der
   Sitzungszeitzone abhängt. Gib je Zeile `id`, `created_at`, den Cast
   `created_at::timestamptz` als `cast_result`, den Zeitpunkt
   `created_at AT TIME ZONE 'UTC'` als `instant_utc` und die Differenz
   beider als `deviation` aus. Führe die Abfrage einmal nach
   `SET TimeZone = 'UTC';` und einmal nach
   `SET TimeZone = 'Europe/Berlin';` aus. Setze die Zeitzone danach mit
   `RESET TimeZone;` zurück.
4. Vergleiche die Summe von `ticket_new.amount` mit der Summe von
   `legacy_ticket.amount`.

## Ergebnis prüfen

Setze die Sitzungszeitzone zuerst auf UTC. Die Prüfung zeigt die Zeitpunkte
dann mit `+00`:

```sql
SET TimeZone = 'UTC';
```

Die sechs übernommenen Tickets:

```sql
SELECT legacy_id, is_closed, created_at, amount
FROM tickets.ticket_new ORDER BY legacy_id;
```

Die Zeitpunkte tragen `+00` und nennen dieselbe Uhrzeit wie
`legacy_ticket.created_at`:

```text
 legacy_id | is_closed |       created_at       | amount
-----------+-----------+------------------------+---------
       101 | f         | 2026-08-21 12:00:00+00 |   19.99
       102 | t         | 2025-12-31 23:30:00+00 |    0.10
       103 | t         | 2025-03-30 00:30:00+00 |    0.20
       104 | f         | 2025-03-30 02:30:00+00 |    0.30
       105 | t         | 2026-06-30 22:15:00+00 | 1250.00
       106 | f         | 2026-01-15 08:00:00+00 |    4.60
(6 rows)
```

Die Summen in beiden Typen:

```sql
SELECT sum(amount) AS numeric_sum,
       (SELECT sum(amount) FROM tickets.legacy_ticket) AS double_sum
FROM tickets.ticket_new;
```

```text
 numeric_sum |     double_sum
-------------+--------------------
     1275.19 | 1275.1899999999998
(1 row)
```

Setze die Zeitzone danach zurück:

```sql
RESET TimeZone;
```

Aufgabe 3 liefert unter `UTC` für jede Zeile die Abweichung `00:00:00`;
`cast_result` und `instant_utc` sind dort gleich. Unter
`Europe/Berlin` weicht der Cast ab:

```text
 id  |     created_at      |      cast_result       |      instant_utc       | deviation
-----+---------------------+------------------------+------------------------+-----------
 101 | 2026-08-21 12:00:00 | 2026-08-21 12:00:00+02 | 2026-08-21 14:00:00+02 | -02:00:00
 102 | 2025-12-31 23:30:00 | 2025-12-31 23:30:00+01 | 2026-01-01 00:30:00+01 | -01:00:00
 103 | 2025-03-30 00:30:00 | 2025-03-30 00:30:00+01 | 2025-03-30 01:30:00+01 | -01:00:00
 104 | 2025-03-30 02:30:00 | 2025-03-30 03:30:00+02 | 2025-03-30 04:30:00+02 | -01:00:00
 105 | 2026-06-30 22:15:00 | 2026-06-30 22:15:00+02 | 2026-07-01 00:15:00+02 | -02:00:00
 106 | 2026-01-15 08:00:00 | 2026-01-15 08:00:00+01 | 2026-01-15 09:00:00+01 | -01:00:00
(6 rows)
```

## Hinweise

`AT TIME ZONE 'UTC'` auf einen `timestamp` nennt ausdrücklich die Zone, in
der die Wandzeit gilt. Das Ergebnis ist derselbe Zeitpunkt, egal unter
welcher Sitzungszeitzone das `INSERT` läuft. Ein bloßer Cast nimmt dagegen
die Sitzungszeitzone. Unter `Europe/Berlin` läge jedes Ticket eine
oder zwei Stunden zu früh, je nachdem, ob zum jeweiligen Zeitpunkt Winter-
oder Sommerzeit galt.

Ticket 104 fällt aus der Reihe. 02:30 Uhr gibt es am 30.03.2025 in
Berlin gar nicht. PostgreSQL deutet die Wandzeit mit dem Offset vor der
Umstellung und zeigt sie als 03:30:00+02 an. Die Abweichung beträgt deshalb
eine Stunde, obwohl der Zeitpunkt schon in der Sommerzeit liegt.

`double precision` speichert binär. Werte wie 0.1 oder 0.2 lassen sich
darin nicht exakt ablegen, deshalb weicht die Summe der sechs Beträge in
der letzten Stelle ab. `numeric(12,2)` rechnet dezimal und liefert 1275.19.

Nach `DROP TABLE` und erneutem Anlegen beginnt die Identity wieder bei 1.
`information_schema.columns` zeigt für `ticket_new.id` den Wert
`is_identity = YES` und für `created_at` den Typ
`timestamp with time zone`.

`loesung.sql` entfernt zu Beginn `ticket_new`, legt `legacy_ticket` mit
dem Block aus dem Ausgangsstand neu an und lässt sich deshalb mehrfach
und ohne vorherige Schritte ausführen.
