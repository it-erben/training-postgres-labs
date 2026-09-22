-- Übung 5: Bonuspunkte je Kunde für die Mengenoperation.
ALTER TABLE verleih.kunde ADD COLUMN bonuspunkte integer NOT NULL DEFAULT 0;
