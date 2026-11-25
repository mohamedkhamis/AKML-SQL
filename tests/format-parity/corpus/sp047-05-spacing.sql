select c.CustomerID from dbo.Customers c inner join dbo.Orders o on o.CustomerID = c.CustomerID left outer join dbo.Shippers s on s.ShipperID = o.ShipVia where o.EmployeeID in (10, 11);
set nocount on;
