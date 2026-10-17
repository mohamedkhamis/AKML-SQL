CREATE FUNCTION top_orders(n int) RETURNS TABLE (id int, total numeric) AS $func$
    select id, total from orders order by total desc limit n;
$func$ LANGUAGE sql STABLE;
