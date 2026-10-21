SELECT  *
FROM    customers
WHERE   phone IS NULL
				AND fax IS NOT NULL
				AND region IS DISTINCT
FROM    'EU' AND city IS NOT DISTINCT
FROM    'Paris' ;
