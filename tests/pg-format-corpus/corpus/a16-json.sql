select data -> 'a' -> 'b', data ->> 'name', data #> '{x,y}', data #>> '{x,y}', jsonb_build_object('k', v, 'n', 1) from docs where data @> '{"a": 1}';
