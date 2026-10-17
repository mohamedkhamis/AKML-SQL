select o.id, c.name, p.name, d.quantity
from orders o
join customers c on c.id = o.customer_id and c.active
join order_details d on d.order_id = o.id
left join products p on p.id = d.product_id and p.discontinued = false
where o.order_date > '2024-01-01';
