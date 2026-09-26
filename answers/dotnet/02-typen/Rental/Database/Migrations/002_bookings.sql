-- Übung 2: Buchungen mit Zeitraum, Zusatzinfo und Gerätezustand.
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TYPE rental.device_condition AS ENUM ('new', 'used', 'defective');

CREATE TABLE rental.booking (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    device_id integer NOT NULL REFERENCES rental.device (id),
    customer_id integer NOT NULL REFERENCES rental.customer (id),
    time_range tstzrange NOT NULL,
    condition_at_pickup rental.device_condition NOT NULL,
    extra_info jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT booking_time_range_not_empty CHECK (NOT isempty(time_range)),
    CONSTRAINT booking_no_overlap
        EXCLUDE USING gist (device_id WITH =, time_range WITH &&)
);

CREATE INDEX booking_customer_idx ON rental.booking (customer_id);
