with src as (select 1 as a, 2 as b) insert into t (a, b) select a, b from src;
