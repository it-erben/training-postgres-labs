-- Übung 6: Rückgabezeitpunkt je Buchung.
ALTER TABLE verleih.buchung ADD COLUMN zurueckgegeben_am timestamptz;
