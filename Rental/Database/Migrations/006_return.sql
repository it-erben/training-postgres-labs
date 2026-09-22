-- Übung 6: Rückgabezeitpunkt je Buchung.
ALTER TABLE rental.booking ADD COLUMN returned_at timestamptz;
