SELECT DISTINCT TOP 25 c.country, c.city FROM dbo.customers c ORDER BY c.country;

SELECT TOP (100) percent p.productname, p.unitprice FROM dbo.products p WHERE p.discontinued = 0;
