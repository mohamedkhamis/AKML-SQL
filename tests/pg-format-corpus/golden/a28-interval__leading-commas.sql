SELECT NOW() - interval '1 day'
     , date '2024-01-01' + 7
     , timestamp '2024-01-01 10:00' AT TIME ZONE 'UTC'
FROM   t;
