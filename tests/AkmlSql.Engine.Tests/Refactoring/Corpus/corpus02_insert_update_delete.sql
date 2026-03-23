INSERT INTO dbo.Customers (CustomerId, Name, Email)
VALUES (1, N'Alice', N'alice@example.com');

UPDATE dbo.Customers
SET Name = N'Alice Smith'
WHERE CustomerId = 1;

DELETE FROM dbo.Customers
WHERE CustomerId = 1;
