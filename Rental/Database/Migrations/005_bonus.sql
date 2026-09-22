-- Übung 5: Bonuspunkte je Kunde für die Mengenoperation.
ALTER TABLE rental.customer ADD COLUMN bonus_points integer NOT NULL DEFAULT 0;
