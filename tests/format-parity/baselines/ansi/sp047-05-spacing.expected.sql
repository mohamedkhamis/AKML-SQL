-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp047-05-spacing profile=ansi
select c.CustomerID
from   dbo.Customers c
inner join dbo.Orders o
    on o.CustomerID = c.CustomerID
left outer join dbo.Shippers s
    on s.ShipperID = o.ShipVia
where  o.EmployeeID in(10, 11);

set nocount on;
