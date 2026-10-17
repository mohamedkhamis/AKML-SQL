select * from a where id in (select a_id from b where b.id in (select b_id from c where c.flag)) order by id;
