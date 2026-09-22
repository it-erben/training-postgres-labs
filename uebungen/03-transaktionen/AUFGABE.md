# Übung 3: Transaktionen, 40 Minuten

## Ziel

Ein Kunde darf höchstens drei Buchungen haben. Die Regel wird in einer
serialisierbaren Transaktion geprüft. Ein Serialisierungskonflikt wiederholt
die gesamte Transaktion mit begrenzter Versuchszahl; Constraint-Verletzungen
werden nicht wiederholt.

## Ausgangslage

`Verleih/Buchungen/Buchungsdienst.cs` enthält einen Stub mit dem Hook
`VorPruefung`, den nur die Tests setzen. `Buchungsablage.AnlegenAsync(conn, tx, ...)`
aus Übung 2 fügt eine Buchung auf einer vorhandenen Verbindung ein.

## Aufgabe

`Buchungsdienst.BucheAsync` öffnet eine Verbindung, beginnt eine Transaktion
mit `IsolationLevel.Serializable`, ruft `VorPruefung` auf, zählt die
Buchungen des Kunden, lehnt ab `BuchungAbgelehnt` ab drei, fügt sonst ein und
bestätigt. Bei `40001` oder `40P01` wird alles bis zu `MaxVersuche` mal
wiederholt. `23P01`, `23505`, `23503` und `23514` werden zu `BuchungsKonflikt`
mit dem Constraint-Namen.

## Anforderungen

| Test                                                          | Prüft                                                                 |
| ------------------------------------------------------------- | --------------------------------------------------------------------- |
| `Buchung_laeuft_in_einer_serialisierbaren_Transaktion`        | `current_setting('transaction_isolation')` im Hook                    |
| `Vierte_Buchung_wird_fachlich_abgelehnt`                      | `BuchungAbgelehnt`, drei Buchungen bleiben                            |
| `Zwei_gleichzeitige_dritte_Buchungen_lassen_genau_eine_durch` | Zwei Tasks, beide sehen zwei Buchungen, am Ende genau drei            |
| `Konflikt_wird_hoechstens_dreimal_versucht`                   | Dauerhafte `40001` im Hook: drei Aufrufe, dann `PostgresException`    |
| `Wiederholung_fuehrt_die_fachliche_Pruefung_erneut_aus`       | Einmaliger Konflikt: Hook zweimal, Buchung gespeichert                |
| `Constraint_Verletzung_wird_nicht_wiederholt`                 | Überlappung: `BuchungsKonflikt` mit `buchung_keine_ueberlappung`, kein zweiter Versuch |
| `Nach_einem_Fehler_bleibt_keine_offene_Transaktion`           | Kein `idle in transaction` in `pg_stat_activity`                      |

## Hinweise

- [Npgsql: Transactions](https://www.npgsql.org/doc/transactions.html)
- [PostgreSQL: Serializable](https://www.postgresql.org/docs/18/transaction-iso.html#XACT-SERIALIZABLE)
- `PostgresException.SqlState` und die Konstanten in `PostgresErrorCodes`.
- Ein `await using` auf der Transaktion rollt bei jeder Ausnahme zurück.
- Der Hook läuft innerhalb der Transaktion; er darf darin Anweisungen
  ausführen und wird bei jedem Versuch erneut aufgerufen.

## Bonus

- `Abbruch_ueber_Token_beendet_die_Serverabfrage`: Das `CancellationToken`
  wird an jede Anweisung durchgereicht; ein Abbruch wird nicht wiederholt.
- `Advisory_Lock_je_Kunde_vermeidet_den_Konflikt`: Mit `MitAdvisoryLock`
  hält die Transaktion `pg_advisory_xact_lock(kunde_id)` vor der Prüfung.

## Fertig, wenn

`dotnet test --filter-trait Uebung=03 --filter-not-trait Stretch=ja` sieben
grüne Tests meldet.
