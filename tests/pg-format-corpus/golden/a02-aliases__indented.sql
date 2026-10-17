SELECT p.id AS product_id,
       p.name product_name,
       p.price * 1.2 AS gross
FROM   products AS p;
