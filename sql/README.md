# PostgreSQL: SQL-Übungsserie

Fünfzehn Übungen an einem Ticketsystem mit Agents, Tickets und Kommentaren,
eine je Kursmodul. Jede Übung baut auf demselben Datenbestand auf und läuft
ohne zusätzliche Werkzeuge im pgAdmin.

## Arbeitsweise

Die Übungen laufen im pgAdmin Query Tool gegen die eigene `app`-Datenbank
im Schema `tickets`. `setup.sql` legt das Schema mit dem Datenbestand an;
ein erneuter Lauf löscht `tickets` samt aller Übungsobjekte darin und baut
es neu auf. Andere Schemas bleiben dabei unberührt. `soundcheck.sql` prüft
in einer Abfrage Verbindung, Rechte und Datenbestand.

Ein Codeblock ist eine Ausführung: Block vollständig markieren und mit F5
ausführen, erst danach der nächste Block. Enthält ein Block mehrere
Anweisungen, schickt pgAdmin sie zusammen. Ohne eigenes `BEGIN` laufen sie
als eine Transaktion, und `Data Output` zeigt nur das Ergebnis der letzten.
Solche Blöcke sind gewollt und im Text so gekennzeichnet. Ein `SET` gilt
bis zum `RESET` oder zum Ende der Sitzung und wirkt auf alle folgenden
Blöcke im selben Query Tool.

## Aufbau einer Übung

Jede `AUFGABE.md` hat denselben Aufbau: Ziel, Ausgangsstand, nummerierte
Aufgaben, Ergebnis prüfen mit echter Abfrage und tatsächlicher Ausgabe, dazu
Hinweise. Übungen, die vor einem Schritt nach einer Vorhersage fragen,
sammeln die Referenzausgaben mit Erklärung am Ende im Abschnitt
"Auflösung". `loesung.sql` ist die Musterlösung und liegt im selben
Verzeichnis. Jeder Abschnitt darin ist eine Ausführung.

## Übungen

Die Nummer einer Übung ist die Nummer des Kursmoduls, zu dem sie gehört.

| Nr. | Übung                                                  |
| --- | ------------------------------------------------------ |
| 00  | [Soundcheck](00-einrichtung/AUFGABE.md)                |
| 01  | [Architektur](01-architektur/AUFGABE.md)               |
| 02  | [Indizes und EXPLAIN](02-explain/AUFGABE.md)           |
| 03  | [Umstieg von Oracle](03-oracle/AUFGABE.md)             |
| 04  | [Transaktionen](04-transaktionen/AUFGABE.md)           |
| 05  | [Moderne SQL-Features](05-moderne-sql/AUFGABE.md)      |
| 06  | [Constraints](06-constraints/AUFGABE.md)               |
| 07  | [Partitionierung](07-partitionierung/AUFGABE.md)       |
| 08  | [Materialized Views](08-materialized-views/AUFGABE.md) |
| 10  | [Datentypen](10-datentypen/AUFGABE.md)                 |
| 11  | [Fremddaten](11-fremddaten/AUFGABE.md)                 |
| 12  | [CNPG aus Anwendersicht](12-cnpg/AUFGABE.md)           |
| 13  | [Verbindungen und Pooling](13-pooling/AUFGABE.md)      |
| 14  | [Hochverfügbarkeit](14-hochverfuegbarkeit/AUFGABE.md)  |
| 15  | [Backup und Recovery](15-backup-recovery/AUFGABE.md)   |
