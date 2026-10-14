SELECT *
FROM   a

       RIGHT JOIN b
           ON a.id = b.id

       FULL OUTER JOIN c
           ON c.id = b.id;
