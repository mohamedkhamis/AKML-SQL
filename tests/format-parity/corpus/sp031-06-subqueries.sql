select c.companyname from dbo.customers c where c.customerid in (select o.customerid from dbo.orders o);
select c.companyname, (select count(*) from dbo.orders o where o.customerid = c.customerid and o.orderdate >= '1997-01-01' and o.shipcountry not in ('USA', 'Canada') and o.freight > (select avg(f.freight) from dbo.orders f where f.shipcountry = c.country)) as ordercount from dbo.customers c;
