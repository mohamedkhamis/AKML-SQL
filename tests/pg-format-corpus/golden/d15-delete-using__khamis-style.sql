DELETE FROM orders o
USING       customers c
WHERE       o.customer_id = c.id
						AND c.closed
RETURNING   o.id ;
