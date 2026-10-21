select o.id, c.name from orders o left outer join customers c on c.id = o.customer_id where c.id is null;
