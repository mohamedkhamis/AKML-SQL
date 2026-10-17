select id::text, price::numeric(10,2), cast(created as date), '2024-01-01'::timestamp with time zone, ('1'::int + 1)::bigint from t;
