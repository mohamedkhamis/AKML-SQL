INSERT INTO counters(key, n)
VALUES('a', 1)
ON CONFLICT (key)
DO UPDATE SET n = counters.n + excluded.n
            , touched = NOW()
WHERE counters.n < 1000
RETURNING n;
