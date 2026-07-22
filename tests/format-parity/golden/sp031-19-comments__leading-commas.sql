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
