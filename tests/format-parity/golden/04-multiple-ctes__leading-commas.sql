WITH active_customers AS (
    SELECT customerid, customername
    FROM   customers
    WHERE  status = 'Active'), recent_orders AS (
    SELECT orderid, customerid, total
    FROM   orders
    WHERE  orderdate > = DATEADD(MONTH, - 6, GETDATE())) SELECT
    c.customername,
    COUNT( o.orderid)
    AS
    order_count,
    SUM( o.total)
    AS
    total_spent
FROM   active_customers c LEFT
JOIN   recent_orders o ON o.customerid = c.customerid
GROUP BY c.customername;
