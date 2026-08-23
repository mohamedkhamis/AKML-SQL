INSERT INTO dbo.customerarchive(customerid, companyname, contactname, country, archivedate
) SELECT
    c.customerid
    , c.companyname
    , c.contactname
    , c.country
    , GETDATE()
FROM        dbo.customers c
WHERE       NOT EXISTS
(
    SELECT 1
    FROM        dbo.orders o
    WHERE       o.customerid = c.customerid AND o.orderdate >= '1997-01-01'
);
