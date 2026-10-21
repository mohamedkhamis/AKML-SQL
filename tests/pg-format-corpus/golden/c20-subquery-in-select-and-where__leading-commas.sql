SELECT (SELECT COUNT(*) FROM b WHERE b.a_id = a.id) AS n
FROM   a
WHERE  (SELECT COUNT(*) FROM c WHERE c.a_id = a.id) > 2;
