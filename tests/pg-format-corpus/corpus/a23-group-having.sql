select category_id, count(*) as n, sum(price) as total from products group by category_id having count(*) > 1 and sum(price) > 100 order by n desc;
