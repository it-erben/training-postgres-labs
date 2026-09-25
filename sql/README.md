# PostgreSQL: SQL-Übungsserie

Fünfzehn Übungen an einem Ticketsystem mit Agents, Tickets und Kommentaren,
eine je Kursmodul. Jede Übung baut auf demselben Datenbestand auf und läuft
ohne zusätzliche Werkzeuge im pgAdmin.

## Arbeitsweise

Die Übungen laufen im pgAdmin Query Tool gegen die eigene `app`-Datenbank
im Schema `tickets`. `setup.sql` legt das Schema mit dem Datenbestand an;
ein erneuter Lauf löscht `tickets` samt aller Übungsobjekte darin und baut
es neu auf. Andere Schemas bleiben dabei unberührt.

## Aufbau einer Übung

Jede `AUFGABE.md` hat denselben Aufbau: Ziel, Ausgangsstand, nummerierte
Aufgaben, Ergebnis prüfen mit echter Abfrage und tatsächlicher Ausgabe, dazu
Hinweise. `loesung.sql` ist die Musterlösung und liegt im selben Verzeichnis.

## Übungen

| Nr. | Übung                                                  | Modul im Kurs                     |
| --- | ------------------------------------------------------ | --------------------------------- |
| 00  | [Einrichtung](00-einrichtung/AUFGABE.md)               | Einstieg und Arbeitsplatz         |
| 01  | [Architektur](01-architektur/AUFGABE.md)               | Architektur                       |
| 02  | [Indizes und EXPLAIN](02-explain/AUFGABE.md)           | Indizes und EXPLAIN               |
| 03  | [Umstieg von Oracle](03-oracle/AUFGABE.md)             | Oracle und PostgreSQL             |
| 04  | [Transaktionen](04-transaktionen/AUFGABE.md)           | Transaktionen, Isolation, Sperren |
| 05  | [Moderne SQL-Features](05-moderne-sql/AUFGABE.md)      | Moderne SQL-Features              |
| 06  | [Constraints](06-constraints/AUFGABE.md)               | Constraints                       |
| 07  | [Partitionierung](07-partitionierung/AUFGABE.md)       | Partitionierung                   |
| 08  | [Materialized Views](08-materialized-views/AUFGABE.md) | Materialized Views                |
| 10  | [Datentypen](10-datentypen/AUFGABE.md)                 | Datentypen                        |
| 11  | [Fremddaten](11-fremddaten/AUFGABE.md)                 | Fremddaten und Integration        |
| 12  | [CNPG aus Anwendersicht](12-cnpg/AUFGABE.md)           | CNPG aus Anwendersicht            |
| 13  | Verbindungen und Pooling                               | Verbindungen und Pooling          |
| 14  | Hochverfügbarkeit                                      | Hochverfügbarkeit, Dos and Don'ts |
| 15  | Backup und Recovery                                    | Backup und Recovery               |
