select * from a join b on (a.x = b.x or a.y = b.y) and a.z between 1 and 10 and exists (select 1 from c where c.id = a.id);
