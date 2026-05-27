-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=08-in-list-long profile=default
SELECT
    productid, productname
FROM products
WHERE category IN( 'Electronics', 'Computers', 'Phones', 'Tablets', 'Accessories', 'Cables', 'Adapters', 'Chargers', 'Headphones', 'Speakers', 'Cameras'
);
