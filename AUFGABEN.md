# Übersicht der Übungen

Sechs Übungen an einer Anwendung, die von Übung zu Übung wächst.
Bonus-Aufgaben sind optional.

| Block | Übung                                                 | Start-Tag         | Lösung              |
| ----- | ----------------------------------------------------- | ----------------- | ------------------- |
| 0     | [Einrichtung](uebungen/00-einrichtung/AUFGABE.md)     | `uebung-00-start` |                     |
| 1     | [Verbindungen](uebungen/01-verbindungen/AUFGABE.md)   | `uebung-01-start` | `uebung-01-loesung` |
| 2     | [Typen und Schema](uebungen/02-typen/AUFGABE.md)      | `uebung-02-start` | `uebung-02-loesung` |
| 3     | [Transaktionen](uebungen/03-transaktionen/AUFGABE.md) | `uebung-03-start` | `uebung-03-loesung` |
| 4     | [Massendaten](uebungen/04-massendaten/AUFGABE.md)     | `uebung-04-start` | `uebung-04-loesung` |
| 5     | [EF Core](uebungen/05-efcore/AUFGABE.md)              | `uebung-05-start` | `uebung-05-loesung` |
| 6     | [Betrieb](uebungen/06-betrieb/AUFGABE.md)             | `uebung-06-start` | `uebung-06-loesung` |
| 7     | Abschluss: Was die Tests nicht prüfen                 |                   |                     |

Jede Aufgabe nennt ihre Anforderungen als Testnamen. Ist der Test grün, ist
die Anforderung erfüllt. Tests mit dem Trait `Stretch=true` sind Bonus.

```sh
dotnet test --filter-trait Exercise=03
dotnet test --filter-trait Exercise=03 --filter-not-trait Stretch=true
```
