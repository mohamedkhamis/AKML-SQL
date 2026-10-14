UPDATE  products p
SET     price = ( SELECT AVG ( price ) FROM products WHERE category_id = p.category_id )
WHERE   p.price IS NULL ;
