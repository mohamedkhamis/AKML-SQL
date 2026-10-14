SELECT ARRAY[1, 2, 3]
     , tags[1]
     , tags[1:2]
     , a = ANY(ARRAY[1, 2])
     , tags @> ARRAY['x']
     , ARRAY_LENGTH(tags, 1)
FROM   t;
