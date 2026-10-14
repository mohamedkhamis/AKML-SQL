DO $$
declare r record;
begin
  for r in select tablename from pg_tables where schemaname = 'public' loop
    raise notice 'table %', r.tablename;
  end loop;
end
$$;
