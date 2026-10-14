SELECT a::int::text
     , (a)::text
     , a::character varying(10)
     , a::double precision
     , a::int[]
     , a::public.mood
     , a::"Mixed Type"
FROM   t;
