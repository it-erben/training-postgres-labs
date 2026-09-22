-- Übung 4: Bestandsbewegungen für den Import und Bestand für den Bonus.
CREATE TABLE rental.stock_movement (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    device_id integer NOT NULL REFERENCES rental.device (id),
    moved_at timestamptz NOT NULL,
    quantity integer NOT NULL CONSTRAINT stock_movement_quantity_positive CHECK (quantity > 0),
    remark text
);

CREATE TABLE rental.stock (
    device_id integer PRIMARY KEY REFERENCES rental.device (id),
    quantity integer NOT NULL
);
