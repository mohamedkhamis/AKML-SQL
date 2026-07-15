SELECT
    GETDATE () AS now,
    ISNULL (o.shippeddate, o.requireddate) AS effectivedate,
    DATEDIFF (DAY, o.orderdate, ISNULL (o.shippeddate, GETDATE ())) AS daystoship,
    UPPER (SUBSTRING (c.companyname, 1, CHARINDEX (' ', c.companyname + ' ') - 1)) AS firstword,
    COALESCE (o.shipregion, c.region, N'n/a') AS region
FROM   dbo.orders o
    INNER JOIN   dbo.customers c
ON c.customerid = o.customerid;
