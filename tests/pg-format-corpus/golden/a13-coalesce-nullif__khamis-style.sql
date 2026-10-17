SELECT  COALESCE ( nickname , first_name , 'anonymous' )
				 , NULLIF ( discount , 0 )
				 , GREATEST ( a , b , c )
				 , LEAST ( a , b )
FROM    people ;
