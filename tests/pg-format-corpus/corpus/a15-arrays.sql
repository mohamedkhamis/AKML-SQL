select array[1, 2, 3], tags[1], tags[1:2], a = any(array[1, 2]), tags @> array['x'], array_length(tags, 1) from t;
