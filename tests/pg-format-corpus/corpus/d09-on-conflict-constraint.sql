insert into t (a) values (1) on conflict on constraint t_pkey do update set a = excluded.a;
