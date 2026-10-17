select name from products
where name like 'Chai%' or name ilike '%tea%' or name not like '%old%' or name similar to '(a|b)%' or name ~ '^[A-Z]' or name !~* 'x$';
