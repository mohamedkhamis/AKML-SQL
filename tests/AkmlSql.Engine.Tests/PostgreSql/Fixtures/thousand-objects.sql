-- Spec 042 (T088): 1,000+ objects in one schema for the catalog-loading timing test (SC-008).
-- 400 tables of five columns each (one a primary key), 300 views over them, 200 functions and 100 procedures.
CREATE SCHEMA bulk;

DO $$
BEGIN
    FOR i IN 1..400 LOOP
        EXECUTE format('CREATE TABLE bulk.t%s (id int PRIMARY KEY, name varchar(40), amount numeric(12,2), created timestamptz DEFAULT now(), note text)', i);
    END LOOP;
    FOR i IN 1..300 LOOP
        EXECUTE format('CREATE VIEW bulk.v%s AS SELECT id, name FROM bulk.t%s', i, i);
    END LOOP;
    FOR i IN 1..200 LOOP
        EXECUTE format('CREATE FUNCTION bulk.f%s(a int, b int DEFAULT 1) RETURNS int LANGUAGE sql AS ''SELECT a + b''', i);
    END LOOP;
    FOR i IN 1..100 LOOP
        EXECUTE format('CREATE PROCEDURE bulk.p%s(IN x int) LANGUAGE sql AS ''SELECT 1''', i);
    END LOOP;
END
$$;
