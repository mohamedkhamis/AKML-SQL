UPDATE    orders o
SET       total = s.sum
FROM      ( SELECT order_id , SUM ( price * qty ) AS sum FROM lines GROUP BY order_id ) s
WHERE     s.order_id = o.id
RETURNING o.id
					 , o.total ;
