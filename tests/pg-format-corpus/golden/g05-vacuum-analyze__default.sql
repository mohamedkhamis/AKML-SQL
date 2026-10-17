VACUUM (VERBOSE, ANALYZE) products;

ANALYZE products (price);

REINDEX TABLE products;

CLUSTER products USING products_pkey;
