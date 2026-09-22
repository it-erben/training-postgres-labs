-- Übung 1: Geräte und Kunden. Wird vom Migrator in das Schema rental eingespielt.
CREATE TABLE rental.device (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL,
    category text NOT NULL
);

CREATE TABLE rental.customer (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL
);

INSERT INTO rental.device (name, category) VALUES
    ('Bohrhammer', 'drills'),
    ('Akkuschrauber', 'drills'),
    ('Stichsäge', 'saws'),
    ('Kreissäge', 'saws'),
    ('Leiter', 'access'),
    ('Gerüst', 'access');

INSERT INTO rental.customer (name) VALUES ('Baufirma Nord'), ('Handwerk Süd');
