-- Übung 2: Buchungen mit Zeitraum, Zusatzinfo und Gerätezustand.
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TYPE verleih.geraetezustand AS ENUM ('neu', 'gebraucht', 'defekt');

CREATE TABLE verleih.buchung (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    geraet_id integer NOT NULL REFERENCES verleih.geraet (id),
    kunde_id integer NOT NULL REFERENCES verleih.kunde (id),
    zeitraum tstzrange NOT NULL,
    zustand_bei_abholung verleih.geraetezustand NOT NULL,
    zusatzinfo jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT buchung_zeitraum_nicht_leer CHECK (NOT isempty(zeitraum)),
    CONSTRAINT buchung_keine_ueberlappung
        EXCLUDE USING gist (geraet_id WITH =, zeitraum WITH &&)
);

CREATE INDEX buchung_kunde_idx ON verleih.buchung (kunde_id);
