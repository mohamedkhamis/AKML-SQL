-- daily revenue rollup
select o.orderdate, sum(o.freight) as freight from dbo.orders o group by o.orderdate;
/*******************************
 * legacy calculation block    *
 * kept for reference          *
 *******************************/
/* multi
   line
   note */
select 1;
