WITH TopCustomers AS (
    SELECT CustomerId, SUM(Amount) AS Total
    FROM dbo.Orders
    GROUP BY CustomerId
    HAVING SUM(Amount) > 1000
)
SELECT c.Name, tc.Total
FROM dbo.Customers c
INNER JOIN TopCustomers tc ON tc.CustomerId = c.CustomerId
WHERE c.IsActive = 1;
