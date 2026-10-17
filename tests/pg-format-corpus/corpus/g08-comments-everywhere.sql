-- the first query
select id, -- the key
       name /* the name */, price
from products -- source
where price > 0 -- only priced

/* second */
-- and a note
order by id;
