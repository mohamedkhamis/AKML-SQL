ALTER TABLE products ADD COLUMN sku text,
    ADD CONSTRAINT sku_unique UNIQUE (sku),
    ALTER COLUMN price SET DEFAULT 0,
    DROP COLUMN legacy;
