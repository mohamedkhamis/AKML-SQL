SELECT o.OrderId, o.CustomerId, o.Amount, o.OrderDate
FROM dbo.Orders o
WHERE o.CustomerId = 42
  AND o.OrderDate >= '2024-01-01';
