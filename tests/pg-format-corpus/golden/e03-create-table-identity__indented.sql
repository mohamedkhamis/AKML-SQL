CREATE TABLE t(id    bigint  GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
               v     text,
               total numeric GENERATED ALWAYS AS (LENGTH(v) * 2) STORED);
