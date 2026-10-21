select * from customers c where exists (select 1 from orders o where o.customer_id = c.id) and not exists (select 1 from refunds r where r.customer_id = c.id);
