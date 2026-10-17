SELECT id
     , CASE WHEN total > 1000 THEN 'large'
           WHEN total > 100 THEN 'medium'
           ELSE 'small'
       END AS size
FROM   orders;
