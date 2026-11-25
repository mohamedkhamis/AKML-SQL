SELECT c.CustomerID
FROM   dbo.Customers c

INNER JOIN dbo.Orders o
    ON o.CustomerID = c.CustomerID

LEFT OUTER JOIN dbo.Shippers s
    ON s.ShipperID = o.ShipVia
WHERE  o.EmployeeID IN(10, 11);


SET nocount ON;
