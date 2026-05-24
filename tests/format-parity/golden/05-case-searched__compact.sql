SELECT orderid, total, CASE WHEN total > 1000 THEN 'Large' WHEN total > 100 THEN 'Medium' WHEN total > 0 THEN 'Small' ELSE 'Empty' END AS size_bucket
FROM orders;
