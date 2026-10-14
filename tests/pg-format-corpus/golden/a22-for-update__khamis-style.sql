SELECT    *
FROM      jobs
WHERE     state = 'new'
ORDER BY  id
LIMIT     1
FOR       update skip locked ;
