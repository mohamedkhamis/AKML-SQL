select * from products where category_id in (select id from categories where active) and id not in (select product_id from discontinued);
