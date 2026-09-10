-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-05-joins profile=ansi
select o.orderid, c.companyname, e.lastname, s.companyname as shipper
from   dbo.orders o
inner join   dbo.customers c
    on c.customerid = o.customerid
left outer join   dbo.employees e
    on e.employeeid = o.employeeid
inner join   dbo.shippers s
    on s.shipperid = o.shipvia
where  o.shipcountry = 'Mexico';
