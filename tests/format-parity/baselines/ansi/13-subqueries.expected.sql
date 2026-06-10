-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=13-subqueries profile=ansi
select c.customerid, c.customername, (
        select COUNT(*)
    from   orders o
    where  o.customerid = c.customerid
)
    as
    order_count, (
        select SUM(total)
    from   orders o
    where
        o.customerid =
        c.customerid
    and
        o.orderdate >=
        DATEADD( YEAR, - 1, GETDATE())
)
    as
    last_year_total
from   customers c
where  exists(
    select 1
    from   orders o
    where  o.customerid = c.customerid and o.total > 1000
)
order by last_year_total desc;
