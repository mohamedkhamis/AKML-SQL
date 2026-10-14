create function tax(price numeric, rate numeric default 0.2) returns numeric language sql immutable as $$ select price * rate $$;
