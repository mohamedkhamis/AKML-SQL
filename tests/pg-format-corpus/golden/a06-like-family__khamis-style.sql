SELECT  name
FROM    products
WHERE   name LIKE 'Chai%'
				OR name ILIKE '%tea%'
				OR name NOT LIKE '%old%'
				OR name SIMILAR TO '(a|b)%'
				OR name ~ '^[A-Z]'
				OR name !~* 'x$' ;
