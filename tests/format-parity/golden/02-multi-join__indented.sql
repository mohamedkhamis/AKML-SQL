select o.orderid, c.customername, sum(d.unitprice * d.quantity) as total
from orders o
inner join customers c on c.customerid = o.customerid
left join orderdetails d on d.orderid = o.orderid
where o.orderdate >= '2025-01-01' and c.country = 'USA'
group by o.orderid, c.customername
having sum(d.unitprice * d.quantity) > 100
order by total desc;
