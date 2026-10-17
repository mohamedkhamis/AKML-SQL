WITH moved
AS (DELETE FROM queue WHERE state = 'done' RETURNING *)
INSERT INTO archive SELECT * FROM moved RETURNING id;
