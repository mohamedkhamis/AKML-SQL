SELECT CASE WHEN a > 0 THEN CASE WHEN b > 0 THEN 'both' ELSE 'a only' END
           ELSE 'none'
       END AS r
FROM   t;
