WITH m AS MATERIALIZED (SELECT * FROM big WHERE flag),
     n AS NOT MATERIALIZED (SELECT * FROM big)
SELECT *
FROM   m

       JOIN n
           USING (id);
