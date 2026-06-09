-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=02-multi-join profile=default
SELECT o.orderid, c.customername, SUM(d.unitprice * d.quantity) AS total
FROM   orders o INNER
JOIN   customers c ON c.customerid = o.customerid LEFT
JOIN   orderdetails d ON d.orderid = o.orderid
WHERE  o.orderdate > = '2025-01-01' AND c.country = 'USA'
GROUP BY o.orderid, c.customername
HAVING SUM(d.unitprice * d.quantity) > 100
ORDER BY total DESC;
