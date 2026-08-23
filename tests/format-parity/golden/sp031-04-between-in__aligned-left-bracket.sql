SELECT o.orderid
FROM   dbo.orders o
WHERE  o.orderdate BETWEEN '1997-01-01' AND '1997-12-31'
AND o.shipcountry IN
(
    'USA',
    'UK',
    'Germany',
    'France'
)
AND o.freight BETWEEN 10.5 AND 200.75;

SELECT od.orderid
FROM   dbo.[order details] od
WHERE  od.productid IN
(
    SELECT p.productid
    FROM   dbo.products p
    WHERE  p.categoryid IN
    (
        1,
        2,
        3
    )
    AND p.unitprice BETWEEN 5 AND 50
    AND p.productname BETWEEN 'Aniseed Syrup' AND 'Wimmers gute Semmelknoedel'
);
