SELECT t.id
     , s.total
FROM   t
JOIN (SELECT id, SUM(x) AS total FROM u GROUP BY id) s
    ON s.id = t.id;
