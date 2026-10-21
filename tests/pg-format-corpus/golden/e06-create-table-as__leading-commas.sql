CREATE TABLE recent AS SELECT * FROM orders WHERE order_date > NOW() - interval '7 days';
