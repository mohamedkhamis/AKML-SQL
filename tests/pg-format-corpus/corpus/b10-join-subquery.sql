select t.id, s.total from t join (select id, sum(x) as total from u group by id) s on s.id = t.id;
