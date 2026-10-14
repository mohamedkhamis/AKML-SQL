create table recent as select * from orders where order_date > now() - interval '7 days';
