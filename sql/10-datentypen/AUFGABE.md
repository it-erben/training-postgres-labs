# SQL-Übung 10: Datentypen

## Ziel

Du überführst sechs Tickets aus einer Altbestandstabelle mit
Oracle-typischen Spaltentypen in eine Tabelle mit PostgreSQL-Typen:
`boolean` statt Zahlenflag, `timestamptz` statt UTC-Wandzeit,
`numeric` statt Gleitkommazahl und eine Identity-Spalte als Schlüssel.
Danach zeigst du, warum ein bloßer Cast von `timestamp` nach
`timestamptz` von der Sitzungszeitzone abhängt.

## Ausgangsstand

Das Schema `tickets` aus [Übung 0](../00-einrichtung/AUFGABE.md) ist
eingerichtet. Die Übung setzt keine andere Übung voraus. Arbeite im Query
Tool mit `Auto commit` an und `Auto rollback on error` aus.

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
    betrag double precision
);
INSERT INTO tickets.legacy_ticket VALUES
    (101, 'Login schlägt fehl',    0, '2026-08-21 12:00:00',   19.99),
    (102, 'Rechnung doppelt',      1, '2025-12-31 23:30:00',    0.1),
    (103, 'Export bricht ab',      1, '2025-03-30 00:30:00',    0.2),
    (104, 'Passwort zurücksetzen', 0, '2025-03-30 02:30:00',    0.3),
    (105, 'Kontoauszug fehlt',     1, '2026-06-30 22:15:00', 1250),
    (106, 'Adresse ändern',        0, '2026-01-15 08:00:00',    4.6);
```

Der Block lässt sich jederzeit erneut ausführen und stellt den
Ausgangsstand wieder her.

## Aufgaben

1. Lege `tickets.ticket_neu` mit diesen Spalten an:

   | Spalte       | Typ und Regel                                     |
   | ------------ | ------------------------------------------------- |
   | `id`         | `bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY` |
   | `legacy_id`  | `bigint UNIQUE NOT NULL`, die ursprüngliche ID    |
   | `subject`    | `text NOT NULL`                                   |
   | `is_closed`  | `boolean NOT NULL`                                |
   | `created_at` | `timestamptz NOT NULL`                            |
   | `betrag`     | `numeric(12,2) NOT NULL`                          |

2. Übernimm alle sechs Zeilen mit einem einzigen `INSERT ... SELECT`.
   `id` erzeugt die Identity, `legacy_id` erhält die alte ID. Wandle
   `closed` mit `closed = 1` in einen Wahrheitswert, `created_at` mit
   `created_at AT TIME ZONE 'UTC'` in einen Zeitpunkt und `betrag` mit
   `round(betrag::numeric, 2)` in einen Dezimalwert.
3. Zeige, dass ein bloßer Cast `created_at::timestamptz` von der
   Sitzungszeitzone abhängt. Berechne dazu je Zeile die Differenz
   `created_at::timestamptz - created_at AT TIME ZONE 'UTC'`, einmal nach
   `SET TimeZone = 'UTC';` und einmal nach `SET TimeZone = 'Europe/Berlin';`.
   Setze die Zeitzone danach mit `RESET TimeZone;` zurück.
4. Vergleiche die Summe von `ticket_neu.betrag` mit der Summe von
   `legacy_ticket.betrag`.

## Ergebnis prüfen

```sql
SET TimeZone = 'UTC';
SELECT legacy_id, is_closed, created_at, betrag
FROM tickets.ticket_neu ORDER BY legacy_id;
SELECT sum(betrag) AS numeric_summe,
       (SELECT sum(betrag) FROM tickets.legacy_ticket) AS double_summe
FROM tickets.ticket_neu;
```

Die erste Abfrage zeigt die sechs übernommenen Tickets. Die Zeitpunkte
tragen `+00` und nennen dieselbe Uhrzeit wie `legacy_ticket.created_at`:

```text
 legacy_id | is_closed |       created_at       | betrag
-----------+-----------+------------------------+---------
       101 | f         | 2026-08-21 12:00:00+00 |   19.99
       102 | t         | 2025-12-31 23:30:00+00 |    0.10
       103 | t         | 2025-03-30 00:30:00+00 |    0.20
       104 | f         | 2025-03-30 02:30:00+00 |    0.30
       105 | t         | 2026-06-30 22:15:00+00 | 1250.00
       106 | f         | 2026-01-15 08:00:00+00 |    4.60
(6 rows)
```

Die zweite vergleicht die Summen:

```text
 numeric_summe |    double_summe
---------------+--------------------
       1275.19 | 1275.1899999999998
(1 row)
```

Aufgabe 3 liefert unter `UTC` für jede Zeile die Abweichung `00:00:00`.
Unter `Europe/Berlin` weicht der Cast ab:

```text
 id  |     created_at      |     cast_ergebnis      |     zeitpunkt_utc      | abweichung
-----+---------------------+------------------------+------------------------+------------
 101 | 2026-08-21 12:00:00 | 2026-08-21 12:00:00+02 | 2026-08-21 14:00:00+02 | -02:00:00
 102 | 2025-12-31 23:30:00 | 2025-12-31 23:30:00+01 | 2026-01-01 00:30:00+01 | -01:00:00
 103 | 2025-03-30 00:30:00 | 2025-03-30 00:30:00+01 | 2025-03-30 01:30:00+01 | -01:00:00
 104 | 2025-03-30 02:30:00 | 2025-03-30 03:30:00+02 | 2025-03-30 04:30:00+02 | -01:00:00
 105 | 2026-06-30 22:15:00 | 2026-06-30 22:15:00+02 | 2026-07-01 00:15:00+02 | -02:00:00
 106 | 2026-01-15 08:00:00 | 2026-01-15 08:00:00+01 | 2026-01-15 09:00:00+01 | -01:00:00
(6 rows)
```

## Hinweise

`AT TIME ZONE 'UTC'` auf einen `timestamp` nennt die Zone ausdrücklich, in
der die Wandzeit gilt. Das Ergebnis ist derselbe Zeitpunkt, egal unter
welcher Sitzungszeitzone das `INSERT` läuft. Ein bloßer Cast nimmt dagegen
die Sitzungszeitzone. Unter `Europe/Berlin` läge jedes Ticket eine oder zwei
Stunden zu früh, je nachdem, ob am jeweiligen Tag Winter- oder Sommerzeit
galt.

Ticket 104 zeigt eine Besonderheit: 02:30 Uhr gibt es am 30.03.2025 in
Berlin nicht. PostgreSQL deutet die Wandzeit mit dem Offset vor der
Umstellung und zeigt sie als 03:30:00+02 an. Die Abweichung beträgt deshalb
eine Stunde, obwohl der Tag schon Sommerzeit hat.

`double precision` speichert binär, Werte wie 0.1 oder 0.2 sind dort nicht
exakt darstellbar. Die Summe der sechs Beträge weicht deshalb in der
letzten Stelle ab. `numeric(12,2)` rechnet dezimal und liefert 1275.19.

Nach `DROP TABLE` und erneutem Anlegen beginnt die Identity wieder bei 1.
`information_schema.columns` zeigt für `ticket_neu.id` den Wert
`is_identity = YES` und für `created_at` den Typ
`timestamp with time zone`.

`loesung.sql` entfernt zu Beginn `ticket_neu` und lässt sich deshalb
mehrfach ausführen. `legacy_ticket` muss vorher aus dem Ausgangsstand
angelegt sein.
