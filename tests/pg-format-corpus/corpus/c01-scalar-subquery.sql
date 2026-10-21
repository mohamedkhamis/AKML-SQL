select id, (select max(total) from orders o where o.customer_id = c.id) as biggest from customers c;
