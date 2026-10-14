select * from generate_series(1, 10) as g(n) join unnest(array[1,2,3]) u on u = g.n;
