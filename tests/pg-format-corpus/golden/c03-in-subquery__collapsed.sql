SELECT * FROM products WHERE category_id IN (SELECT id FROM categories WHERE active) AND id NOT IN (SELECT product_id FROM discontinued);
