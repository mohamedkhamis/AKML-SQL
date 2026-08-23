WITH ordertotals(orderid, customerid, linecount, totalvalue
) AS (
    SELECT od.orderid,
    o.customerid,
    COUNT(*),
    SUM(od.unitprice * od.quantity * (1 - od.discount)
    )
    FROM   dbo.[order details] od INNER JOIN dbo.orders o ON o.orderid = od.orderid
    GROUP BY od.orderid, o.customerid
    ),

    customerranks(customerid, RANK) AS (
    SELECT customerid, ROW_NUMBER() OVER ( ORDER BY SUM(totalvalue) DESC)
    FROM   ordertotals
    GROUP BY customerid
    )
SELECT cr.customerid, cr.RANK, ot.totalvalue
FROM   customerranks cr
INNER JOIN   ordertotals ot
    ON ot.customerid = cr.customerid
WHERE  cr.RANK <= 10;
