SELECT c.id,
       l.*
FROM   customers c

       CROSS JOIN LATERAL (SELECT * FROM orders o WHERE o.customer_id = c.id ORDER BY o.id DESC LIMIT 3) l;
