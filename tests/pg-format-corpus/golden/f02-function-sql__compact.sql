CREATE FUNCTION tax(price numeric, rate numeric DEFAULT 0.2) RETURNS numeric LANGUAGE sql IMMUTABLE AS $$ select price * rate $$;
