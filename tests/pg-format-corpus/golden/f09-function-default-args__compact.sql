CREATE FUNCTION f(a int, b text DEFAULT 'x', VARIADIC rest int[] DEFAULT '{}') RETURNS void AS 'select 1' LANGUAGE sql;
