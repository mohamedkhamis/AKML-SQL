with moved as (delete from queue where state = 'done' returning *) insert into archive select * from moved returning id;
