SELECT  CASE status WHEN 'N' THEN 'new' WHEN 'P' THEN 'paid' ELSE 'other' END
FROM    orders ;
