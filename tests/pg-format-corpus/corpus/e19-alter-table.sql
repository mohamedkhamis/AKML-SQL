alter table products add column sku text, add constraint sku_unique unique (sku), alter column price set default 0, drop column legacy;
