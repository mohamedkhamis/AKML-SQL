create function f(a int, b text default 'x', variadic rest int[] default '{}') returns void as 'select 1' language sql;
