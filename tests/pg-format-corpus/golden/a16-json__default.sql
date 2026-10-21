SELECT data -> 'a' -> 'b',
       data ->> 'name',
       data #> '{x,y}',
       data #>> '{x,y}',
       JSONB_BUILD_OBJECT('k', v, 'n', 1)
FROM   docs
WHERE  data @> '{"a": 1}';
