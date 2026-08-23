UPDATE dbo.products SET unitprice = unitprice * 1.1, reorderlevel = reorderlevel + 5 WHERE categoryid = 2 AND discontinued = 0;

UPDATE p
SET    p.unitsinstock = p.unitsinstock - od.quantity
FROM   dbo.products p
INNER JOIN   dbo.[order details] od ON od.productid = p.productid
WHERE  od.orderid = 11077;

DELETE FROM dbo.[order details] WHERE orderid = 10248 AND productid IN ( 11, 42, 72 );
