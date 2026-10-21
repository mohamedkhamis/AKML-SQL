WITH
	src AS ( SELECT 1 AS a , 2 AS b )
INSERT INTO t ( a , b )
SELECT  a
				 , b
FROM    src ;
