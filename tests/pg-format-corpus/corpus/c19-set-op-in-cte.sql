with u as (select id from a union select id from b) select count(*) from u;
