create table t (id bigint generated always as identity primary key, v text, total numeric generated always as (length(v) * 2) stored);
