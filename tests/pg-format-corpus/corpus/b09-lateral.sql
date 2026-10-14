select c.id, l.* from customers c cross join lateral (select * from orders o where o.customer_id = c.id order by o.id desc limit 3) l;
