CREATE OR REPLACE FUNCTION log_it() RETURNS TRIGGER LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
begin
  insert into log (at, what) values (now(), tg_op);
  return new;
end
$$ ;
