-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=13-subqueries profile=default
SELECT c.customerid, c.customername, (
        SELECT COUNT(*)
    FROM   orders o
    WHERE  o.customerid = c.customerid
) AS order_count, (
        SELECT SUM(total)
    FROM   orders o
    WHERE  o.customerid = c.customerid
    AND o.orderdate >= DATEADD(YEAR, -1, GETDATE())
) AS last_year_total
FROM   customers c
WHERE  EXISTS(
    SELECT 1
    FROM   orders o
    WHERE  o.customerid = c.customerid AND o.total > 1000
)
ORDER BY last_year_total DESC;
