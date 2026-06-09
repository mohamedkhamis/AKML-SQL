-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=02-multi-join profile=ansi
select o.orderid, c.customername, SUM(d.unitprice * d.quantity) as total
from   orders o inner
join   customers c on c.customerid = o.customerid left
join   orderdetails d on d.orderid = o.orderid
where  o.orderdate >= '2025-01-01' and c.country = 'USA'
group by o.orderid, c.customername
having SUM(d.unitprice * d.quantity) > 100
order by total desc;
