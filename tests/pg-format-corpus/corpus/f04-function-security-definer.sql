create or replace function log_it() returns trigger language plpgsql security definer set search_path = public as $$
begin
  insert into log (at, what) values (now(), tg_op);
  return new;
end
$$;
