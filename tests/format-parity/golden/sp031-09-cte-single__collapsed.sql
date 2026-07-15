WITH recentorders
AS (
    SELECT o.orderid, o.customerid, o.orderdate FROM dbo.orders o WHERE o.orderdate >= '1998-01-01')
SELECT r.customerid, COUNT (*) AS cnt FROM recentorders r GROUP BY r.customerid HAVING COUNT (*) > 3 ORDER BY cnt DESC;
