-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-19-comments profile=ansi
-- daily revenue rollup
select o.orderdate, SUM(o.freight) as freight
from   dbo.orders o
group by o.orderdate;
/*******************************
 * legacy calculation block    *
 * kept for reference          *
 *******************************/
/* multi
   line
   note */

select 1;
