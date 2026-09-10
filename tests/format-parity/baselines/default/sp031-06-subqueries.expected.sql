-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-06-subqueries profile=default
SELECT c.companyname
FROM   dbo.customers c
WHERE  c.customerid IN(
    SELECT o.customerid FROM dbo.orders o);

SELECT c.companyname, (
        SELECT COUNT(*)
    FROM   dbo.orders o
    WHERE  o.customerid = c.customerid
    AND o.orderdate >= '1997-01-01'
    AND o.shipcountry NOT IN(
    'USA',
    'Canada'
)
    AND o.freight > (
            SELECT AVG(f.freight)
        FROM   dbo.orders f
        WHERE  f.shipcountry = c.country
    )
) AS ordercount
FROM   dbo.customers c;
