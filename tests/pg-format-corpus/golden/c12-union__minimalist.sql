SELECT id,
       'a' AS src
FROM   a
UNION ALL
SELECT id,
       'b'
FROM   b
UNION
SELECT id,
       'c'
FROM   c;
