update products p set price = (select avg(price) from products where category_id = p.category_id) where p.price is null;
