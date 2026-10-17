alter table t rename to t2; alter table t2 owner to app; alter index i set tablespace fast; alter sequence s restart with 1;
