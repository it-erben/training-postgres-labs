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
Hinweise. Die Musterlösung liegt getrennt von der Aufgabe unter
`answers/sql/NN-thema/loesung.sql`. Jeder Abschnitt darin ist eine
Ausführung.

## Übungen

Die Nummer einer Übung ist die Nummer des Kursmoduls, zu dem sie gehört.

| Nr. | Übung                                                  |
| --- | ------------------------------------------------------ |
| 00  | [Soundcheck](00-einrichtung/AUFGABE.md)                |
| 01  | [Architektur](01-architektur/AUFGABE.md)               |
| 02  | [Umstieg von Oracle](02-oracle/AUFGABE.md)             |
| 03  | [Transaktionen](03-transaktionen/AUFGABE.md)           |
| 04  | [Indizes und EXPLAIN](04-explain/AUFGABE.md)           |
| 05  | [Datentypen](05-datentypen/AUFGABE.md)                 |
| 06  | [Moderne SQL-Features](06-moderne-sql/AUFGABE.md)      |
| 07  | [Constraints](07-constraints/AUFGABE.md)               |
| 08  | [Partitionierung](08-partitionierung/AUFGABE.md)       |
| 09  | [Materialized Views](09-materialized-views/AUFGABE.md) |
| 11  | [CNPG aus Anwendersicht](11-cnpg/AUFGABE.md)           |
| 12  | [Verbindungen und Pooling](12-pooling/AUFGABE.md)      |
| 13  | [Hochverfügbarkeit](13-hochverfuegbarkeit/AUFGABE.md)  |
| 14  | [Backup und Recovery](14-backup-recovery/AUFGABE.md)   |
| 15  | [Fremddaten](15-fremddaten/AUFGABE.md)                 |
