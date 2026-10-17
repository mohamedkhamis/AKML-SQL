SELECT *
FROM   a

       JOIN b
           ON (a.x = b.x OR a.y = b.y)
           AND a.z BETWEEN 1 AND 10
           AND EXISTS(SELECT 1 FROM c WHERE c.id = a.id);
