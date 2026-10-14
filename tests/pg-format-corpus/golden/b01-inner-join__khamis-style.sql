SELECT  o.id
				 , c.name
FROM    orders o
				INNER JOIN customers c ON c.id = o.customer_id ;
