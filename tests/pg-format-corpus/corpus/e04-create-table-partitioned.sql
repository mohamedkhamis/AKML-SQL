create table measurements (ts timestamptz not null, device int, value double precision) partition by range (ts);
