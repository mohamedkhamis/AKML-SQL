SELECT  o.id
				 , c.name
FROM    orders o
				LEFT OUTER JOIN customers c ON c.id = o.customer_id
WHERE   c.id IS NULL ;
