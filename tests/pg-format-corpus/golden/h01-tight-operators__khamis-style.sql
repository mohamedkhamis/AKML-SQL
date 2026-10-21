SELECT  a = b
				 , a <> b
				 , a <= b
				 , a >= b
				 , a < b
				 , a > b
				 , a + b
				 , a - b
				 , a * b
				 , a / b
				 , a % b
				 , a || b
FROM    t
WHERE   x = 1
				AND y < 2 ;
