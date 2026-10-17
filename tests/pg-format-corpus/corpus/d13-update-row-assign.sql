update t set (a, b) = (1, 2), (c, d) = (select x, y from u where u.id = t.id) where id = 1;
