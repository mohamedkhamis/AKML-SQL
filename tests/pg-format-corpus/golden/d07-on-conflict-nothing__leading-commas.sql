INSERT INTO counters(key, n) VALUES('a', 1) ON CONFLICT (key) DO NOTHING;
