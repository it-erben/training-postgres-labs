# Übung 2: Typen und Schema, 35 Minuten

## Ziel

Buchungen mit Zeitraum, Zusatzinfo und Gerätezustand werden verlustfrei
zwischen .NET und PostgreSQL abgebildet. Der Server verhindert überlappende
Buchungen desselben Geräts.

## Ausgangslage

`Verleih/Datenbank/Migrationen/002_buchungen.sql` ist leer. `Buchung`,
`Zusatzinfo` und `Geraetezustand` sind vorgegeben. `Buchungsablage` enthält
Stubs. `Datenquelle` aus Übung 1 kennt noch keinen Enum und kein JSON.

## Aufgabe

1. Migration `002_buchungen.sql`: Enum-Typ `verleih.geraetezustand` mit den
   Werten `neu`, `gebraucht`, `defekt`; Tabelle `verleih.buchung` mit
   `zeitraum tstzrange`, `zustand_bei_abholung`, `zusatzinfo jsonb` und einem
   `EXCLUDE`-Constraint, der Überlappungen je Gerät verhindert. `btree_gist`
   erlaubt `=` auf `geraet_id` im selben Index.
2. `Datenquelle.Erzeuge`: Enum-Zuordnung und System.Text.Json mit camelCase.
3. `Buchungsablage`: anlegen, laden, nach Abholort suchen. Zeitpunkte gehen
   mit explizitem `NpgsqlDbType.TimestampTz` an den Server.

## Anforderungen

| Test                                                          | Prüft                                                           |
| ------------------------------------------------------------- | --------------------------------------------------------------- |
| `Buchung_speichert_Zeitraum_als_tstzrange`                    | Typ, Grenzen `[)`                                               |
| `Zeitpunkte_kommen_als_Utc_zurueck`                           | `DateTime.Kind = Utc` beim Lesen                                |
| `Lokale_Zeitpunkte_werden_abgewiesen`                         | `Kind = Local` löst `ArgumentException` aus, nichts gespeichert |
| `Ueberlappende_Buchung_desselben_Geraets_scheitert_mit_23P01` | `EXCLUDE` mit Name                                              |
| `Angrenzende_Buchungen_sind_erlaubt`                          | `[10:00,12:00)` und `[12:00,14:00)`                             |
| `Ueberlappung_anderer_Geraete_ist_erlaubt`                    | `geraet_id WITH =` im Constraint                                |
| `Zusatzinfo_wird_als_jsonb_mit_camelCase_gespeichert`         | `zusatzinfo ->> 'abholort'`                                     |
| `Suche_nach_Zusatzinfo_verwendet_jsonb_Parameter`             | `@>` mit `NpgsqlDbType.Jsonb`                                   |
| `Geraetezustand_ist_ein_Enum_auf_beiden_Seiten`               | `pg_type.typtype = 'e'`, .NET-Enum beim Lesen                   |

## Hinweise

- [Npgsql: Date and Time](https://www.npgsql.org/doc/types/datetime.html)
- [Npgsql: Enums](https://www.npgsql.org/doc/types/enums_and_composites.html)
- [Npgsql: JSON](https://www.npgsql.org/doc/types/json.html)
- [PostgreSQL: Range Types und Constraints](https://www.postgresql.org/docs/18/rangetypes.html#RANGETYPES-CONSTRAINT)
- Ohne expliziten `NpgsqlDbType` sendet Npgsql ein `DateTime` mit
  `Kind = Local` als `timestamp`; der Server castet dann in seiner
  Sitzungszeitzone. Der Test verlangt die Ausnahme statt der stillen
  Umdeutung.
- `tstzrange($3, $4, '[)')` baut den Bereich auf dem Server; `$4` darf NULL sein.

## Bonus

`Zeitraum_ohne_Ende_bedeutet_offene_Buchung`: Eine Buchung ohne Ende sperrt
das Gerät für alle späteren Buchungen.

## Fertig, wenn

`dotnet test --filter-trait Uebung=02 --filter-not-trait Stretch=ja` neun
grüne Tests meldet und die Tests von Übung 1 weiterhin grün sind.
