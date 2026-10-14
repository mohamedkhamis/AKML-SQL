SELECT  *
FROM    t
WHERE   x > ANY ( SELECT y FROM u )
				AND x <= ALL ( SELECT z FROM w ) ;
