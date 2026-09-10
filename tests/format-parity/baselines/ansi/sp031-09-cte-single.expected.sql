-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-09-cte-single profile=ansi
with recentorders as (
    select o.orderid, o.customerid, o.orderdate
    from   dbo.orders o
    where  o.orderdate >= '1998-01-01'
)
select r.customerid, COUNT(*) as cnt
from   recentorders r
group by r.customerid
having COUNT(*) > 3
order by cnt desc;
