# Übersicht der Übungen

Sechs Übungen an einer Anwendung, die von Übung zu Übung wächst.
Bonus-Aufgaben sind optional.

| Block | Übung                                               | Lösung                             |
| ----- | --------------------------------------------------- | ---------------------------------- |
| 0     | [Einrichtung](dotnet/00-einrichtung/AUFGABE.md)     |                                    |
| 1     | [Verbindungen](dotnet/01-verbindungen/AUFGABE.md)   | `answers/dotnet/01-verbindungen/`  |
| 2     | [Typen und Schema](dotnet/02-typen/AUFGABE.md)      | `answers/dotnet/02-typen/`         |
| 3     | [Transaktionen](dotnet/03-transaktionen/AUFGABE.md) | `answers/dotnet/03-transaktionen/` |
| 4     | [Massendaten](dotnet/04-massendaten/AUFGABE.md)     | `answers/dotnet/04-massendaten/`   |
| 5     | [EF Core](dotnet/05-efcore/AUFGABE.md)              | `answers/dotnet/05-efcore/`        |
| 6     | [Betrieb](dotnet/06-betrieb/AUFGABE.md)             | `answers/dotnet/06-betrieb/`       |
| 7     | Abschluss: Was die Tests nicht prüfen               |                                    |

Jede Aufgabe nennt ihre Anforderungen als Testnamen. Ist der Test grün, ist
die Anforderung erfüllt. Tests mit dem Trait `Stretch=true` sind Bonus.

```sh
dotnet test --filter-trait Exercise=03
dotnet test --filter-trait Exercise=03 --filter-not-trait Stretch=true
```
