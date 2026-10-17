SELECT  id
				 , ROW_NUMBER ( ) OVER ( PARTITION BY dept ORDER BY salary DESC ) rn
				 , LAG ( salary ) OVER w
				 , SUM ( salary ) OVER ( PARTITION BY dept )
FROM    emp
WINDOW  w AS ( ORDER BY id ) ;
