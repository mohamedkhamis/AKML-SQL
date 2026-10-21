(select id from a order by id limit 5) union all (select id from b order by id desc limit 5) order by 1;
