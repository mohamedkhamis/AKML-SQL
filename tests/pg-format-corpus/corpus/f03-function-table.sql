create function top_orders(n int) returns table (id int, total numeric) as $func$
    select id, total from orders order by total desc limit n;
$func$ language sql stable;
