SELECT id
     , (SELECT MAX(total) FROM orders o WHERE o.customer_id = c.id) AS biggest
FROM   customers c;
