SELECT  *
				 , t.*
				 , COUNT ( * )
				 , COUNT ( DISTINCT x )
FROM    t ;
