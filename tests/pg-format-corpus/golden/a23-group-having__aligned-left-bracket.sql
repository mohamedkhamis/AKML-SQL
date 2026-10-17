SELECT   category_id
       , COUNT(*) AS n
       , SUM(price) AS total
FROM     products
GROUP BY category_id
HAVING   COUNT(*) > 1
AND SUM(price) > 100
ORDER BY n DESC;
