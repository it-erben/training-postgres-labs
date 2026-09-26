-- Übung 6: Rückgabezeitpunkt je Buchung. rental.booking entsteht erst mit
-- der Lösung von Übung 2; bis dahin überspringt IF EXISTS die Änderung.
ALTER TABLE IF EXISTS rental.booking ADD COLUMN returned_at timestamptz;
