select o.id, c.name from orders o inner join customers c on c.id = o.customer_id;
