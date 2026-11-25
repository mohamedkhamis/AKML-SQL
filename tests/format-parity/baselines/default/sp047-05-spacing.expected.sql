-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp047-05-spacing profile=default
SELECT c.CustomerID
FROM   dbo.Customers c
INNER JOIN dbo.Orders o
    ON o.CustomerID = c.CustomerID
LEFT OUTER JOIN dbo.Shippers s
    ON s.ShipperID = o.ShipVia
WHERE  o.EmployeeID IN(10, 11);

SET nocount ON;
