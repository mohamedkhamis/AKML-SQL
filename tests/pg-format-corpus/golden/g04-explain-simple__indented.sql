EXPLAIN ANALYZE
SELECT COUNT(*)
FROM   orders o

       JOIN customers c
           ON c.id = o.customer_id;
