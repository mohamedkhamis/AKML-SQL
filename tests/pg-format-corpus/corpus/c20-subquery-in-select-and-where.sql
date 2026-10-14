select (select count(*) from b where b.a_id = a.id) as n from a where (select count(*) from c where c.a_id = a.id) > 2;
