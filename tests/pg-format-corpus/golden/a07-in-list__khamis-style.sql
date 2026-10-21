SELECT  *
FROM    products
WHERE   category_id IN ( 1 , 2 , 3 , 4 )
				AND supplier_id NOT IN ( 7 , 8 ) ;
