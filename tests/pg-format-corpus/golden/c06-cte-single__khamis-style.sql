WITH
	recent AS ( SELECT * FROM orders WHERE order_date > NOW ( ) - interval '30 days' )
SELECT  COUNT ( * )
FROM    recent ;
