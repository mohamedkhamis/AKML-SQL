SELECT o.id,
       c.name,
       p.name,
       d.quantity
FROM   orders o
JOIN customers c
    ON c.id = o.customer_id
    AND c.active
JOIN order_details d
    ON d.order_id = o.id
LEFT JOIN products p
    ON p.id = d.product_id
    AND p.discontinued = FALSE
WHERE  o.order_date > '2024-01-01';
