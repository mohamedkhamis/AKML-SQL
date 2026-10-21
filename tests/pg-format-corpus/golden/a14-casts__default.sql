SELECT id::text,
       price::numeric(10, 2),
       CAST(created AS date),
       '2024-01-01'::timestamp with time zone,
       ('1'::int + 1)::bigint
FROM   t;
