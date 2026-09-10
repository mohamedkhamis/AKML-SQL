-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-19-comments profile=default
-- daily revenue rollup
SELECT o.orderdate, SUM(o.freight) AS freight
FROM   dbo.orders o
GROUP BY o.orderdate;
/*******************************
 * legacy calculation block    *
 * kept for reference          *
 *******************************/
/* multi
   line
   note */

SELECT 1;
