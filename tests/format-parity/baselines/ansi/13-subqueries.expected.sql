-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=13-subqueries profile=ansi
select c.customerid, c.customername,
(select count(*) from orders o where o.customerid = c.customerid) as order_count,
(select sum(total) from orders o where o.customerid = c.customerid and o.orderdate >= dateadd(year, -1, getdate())) as last_year_total
from customers c
where exists (select 1 from orders o where o.customerid = c.customerid and o.total > 1000)
order by last_year_total desc;
