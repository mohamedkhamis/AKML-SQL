UPDATE t
SET    (a, b) = (1, 2),
       (c, d) = (SELECT x, y FROM u WHERE u.id = t.id)
WHERE  id = 1;
