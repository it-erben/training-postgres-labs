-- Übung 4: Bestandsbewegungen für den Import und Bestand für den Bonus.
CREATE TABLE verleih.bestandsbewegung (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    geraet_id integer NOT NULL REFERENCES verleih.geraet (id),
    zeitpunkt timestamptz NOT NULL,
    menge integer NOT NULL CONSTRAINT bestandsbewegung_menge_positiv CHECK (menge > 0),
    bemerkung text
);

CREATE TABLE verleih.bestand (
    geraet_id integer PRIMARY KEY REFERENCES verleih.geraet (id),
    menge integer NOT NULL
);
