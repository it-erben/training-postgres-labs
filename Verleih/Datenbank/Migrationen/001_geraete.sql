-- Übung 1: Geräte und Kunden. Wird vom Migrator in das Schema verleih eingespielt.
CREATE TABLE verleih.geraet (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL,
    kategorie text NOT NULL
);

CREATE TABLE verleih.kunde (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL
);

INSERT INTO verleih.geraet (name, kategorie) VALUES
    ('Bohrhammer', 'Bohrer'),
    ('Akkuschrauber', 'Bohrer'),
    ('Stichsäge', 'Sägen'),
    ('Kreissäge', 'Sägen'),
    ('Leiter', 'Zugang'),
    ('Gerüst', 'Zugang');

INSERT INTO verleih.kunde (name) VALUES ('Baufirma Nord'), ('Handwerk Süd');
