INSERT INTO sales.top_customers ( customer_id , company_name , total_spent )
SELECT    c.customer_id
					 , c.company_name
					 , SUM ( o.total )
FROM      sales.customers c
					JOIN sales.orders o ON o.customer_id = c.customer_id
GROUP BY  c.customer_id
					 , c.company_name
HAVING    SUM ( o.total ) > 1000 ;
