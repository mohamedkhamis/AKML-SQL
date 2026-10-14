SELECT a, b, SUM(c) FROM t GROUP BY rollup (a, b);
