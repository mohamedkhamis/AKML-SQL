insert into counters (key, n) values ('a', 1) on conflict (key) do update set n = counters.n + excluded.n, touched = now() where counters.n < 1000 returning n;
