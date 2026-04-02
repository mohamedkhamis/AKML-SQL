SELECT
    c.CustomerId,
    c.Name,
    COUNT(o.OrderId)    AS OrderCount,
    SUM(o.Amount)       AS TotalAmount,
    MAX(o.OrderDate)    AS LastOrderDate
FROM dbo.Customers c
INNER JOIN dbo.Orders o ON o.CustomerId = c.CustomerId
WHERE o.Amount > 100
  AND c.IsActive = 1
GROUP BY c.CustomerId, c.Name
HAVING COUNT(o.OrderId) > 2
ORDER BY TotalAmount DESC;
