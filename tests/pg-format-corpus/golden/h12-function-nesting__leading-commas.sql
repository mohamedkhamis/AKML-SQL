SELECT COALESCE(NULLIF(TRIM(BOTH ' ' FROM UPPER(name)), ''), 'n/a')
     , SUBSTRING(s FROM 2 FOR 3)
     , POSITION('a' IN s)
     , EXTRACT(year FROM d)
     , OVERLAY(s PLACING 'x' FROM 2)
FROM   t;
