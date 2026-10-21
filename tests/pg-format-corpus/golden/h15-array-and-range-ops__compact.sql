SELECT a && b, a <@ b, a @> b, r1 -|- r2, int4range(1, 5) * int4range(3, 9), tsv @@ TO_TSQUERY('a & b') FROM t;
