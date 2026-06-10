-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=04-multiple-ctes profile=ansi
with active_customers as (
    select customerid, customername
    from   customers
    where  status = 'Active'
), recent_orders as (
    select orderid, customerid, total
    from   orders
    where  orderdate >= DATEADD(MONTH, -6, GETDATE())
)
select
    c.customername,
    COUNT(o.orderid) as order_count,
    SUM(o.total) as total_spent
from   active_customers c
left join   recent_orders o on o.customerid = c.customerid
group by c.customername;
