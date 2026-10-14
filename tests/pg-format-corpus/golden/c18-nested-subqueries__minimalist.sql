SELECT   *
FROM     a
WHERE    id IN(SELECT a_id FROM b WHERE b.id IN(SELECT b_id FROM c WHERE c.flag))
ORDER BY id;
