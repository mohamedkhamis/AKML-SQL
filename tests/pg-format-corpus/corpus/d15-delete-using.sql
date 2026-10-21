delete from orders o using customers c where o.customer_id = c.id and c.closed returning o.id;
