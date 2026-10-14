with recent as (select * from orders where order_date > now() - interval '30 days') select count(*) from recent;
