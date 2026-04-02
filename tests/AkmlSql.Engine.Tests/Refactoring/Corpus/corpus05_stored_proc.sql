DECLARE @CustomerId INT = 123
DECLARE @StartDate  DATE = '2024-01-01'

SELECT
    o.CustomerId,
    COUNT(o.OrderId)    AS OrderCount,
    SUM(o.Amount)       AS TotalAmount,
    MAX(o.OrderDate)    AS LastOrderDate
FROM dbo.Orders o
WHERE o.CustomerId = @CustomerId
  AND o.OrderDate >= @StartDate
GROUP BY o.CustomerId
