-- Spec 042 live-test fixture: a small Northwind-shaped schema in PostgreSQL's own conventions
-- (lower-case snake_case names, identity columns, comments, a second schema for search-path checks).
-- Authored for this repository; it is not the Microsoft Northwind data.

CREATE TABLE categories (
    category_id   integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    category_name varchar(40) NOT NULL,
    description   text
);
COMMENT ON TABLE categories IS 'Product categories';
COMMENT ON COLUMN categories.category_name IS 'Display name of the category';

CREATE TABLE suppliers (
    supplier_id  integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_name varchar(60) NOT NULL,
    country      varchar(30)
);

CREATE TABLE customers (
    customer_id  char(5) PRIMARY KEY,
    company_name varchar(60) NOT NULL,
    city         varchar(30),
    country      varchar(30)
);

CREATE TABLE employees (
    employee_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    last_name   varchar(30) NOT NULL,
    first_name  varchar(30) NOT NULL
);

CREATE TABLE shippers (
    shipper_id   integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_name varchar(40) NOT NULL
);

CREATE TABLE products (
    product_id        integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_name      varchar(60) NOT NULL,
    supplier_id       integer REFERENCES suppliers (supplier_id),
    category_id       integer REFERENCES categories (category_id),
    unit_price        numeric(10, 2) NOT NULL DEFAULT 0,
    units_in_stock    integer NOT NULL DEFAULT 0,
    discontinued      boolean NOT NULL DEFAULT false,
    tags              text[],
    attributes        jsonb
);

CREATE TABLE orders (
    order_id     integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    customer_id  char(5) REFERENCES customers (customer_id),
    employee_id  integer REFERENCES employees (employee_id),
    shipper_id   integer REFERENCES shippers (shipper_id),
    order_date   timestamptz NOT NULL DEFAULT now(),
    freight      numeric(10, 2)
);
CREATE INDEX ix_orders_customer ON orders (customer_id);

CREATE TABLE order_details (
    order_id   integer NOT NULL REFERENCES orders (order_id),
    product_id integer NOT NULL REFERENCES products (product_id),
    unit_price numeric(10, 2) NOT NULL,
    quantity   integer NOT NULL,
    PRIMARY KEY (order_id, product_id)
);

CREATE VIEW v_order_totals AS
    SELECT o.order_id, o.customer_id, sum(d.unit_price * d.quantity) AS total
    FROM orders o JOIN order_details d ON d.order_id = o.order_id
    GROUP BY o.order_id, o.customer_id;

CREATE MATERIALIZED VIEW mv_customer_orders AS
    SELECT customer_id, count(*) AS order_count FROM orders GROUP BY customer_id;

CREATE SEQUENCE order_ref_seq;

CREATE FUNCTION order_total(p_order_id integer) RETURNS numeric
LANGUAGE sql STABLE AS $$ SELECT sum(unit_price * quantity) FROM order_details WHERE order_id = p_order_id $$;

CREATE PROCEDURE discontinue_product(p_product_id integer)
LANGUAGE sql AS $$ UPDATE products SET discontinued = true WHERE product_id = p_product_id $$;

CREATE SCHEMA sales;
CREATE TABLE sales.orders (
    id   integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    note text
);
COMMENT ON TABLE sales.orders IS 'Shadow of public.orders for search-path tests';

INSERT INTO categories (category_name, description) VALUES ('Beverages', 'Soft drinks, coffees, teas'), ('Condiments', 'Sauces and spreads');
INSERT INTO suppliers (company_name, country) VALUES ('Exotic Liquids', 'UK'), ('New Orleans Cajun Delights', 'USA');
INSERT INTO customers (customer_id, company_name, city, country) VALUES
    ('ALFKI', 'Alfreds Futterkiste', 'Berlin', 'Germany'),
    ('ANATR', 'Ana Trujillo Emparedados', 'México D.F.', 'Mexico');
INSERT INTO employees (last_name, first_name) VALUES ('Davolio', 'Nancy'), ('Fuller', 'Andrew');
INSERT INTO shippers (company_name) VALUES ('Speedy Express'), ('United Package');
INSERT INTO products (product_name, supplier_id, category_id, unit_price, units_in_stock, tags, attributes) VALUES
    ('Chai', 1, 1, 18.00, 39, ARRAY['tea', 'hot'], '{"organic": true}'),
    ('Chang', 1, 1, 19.00, 17, ARRAY['beer'], '{"organic": false}'),
    ('Aniseed Syrup', 2, 2, 10.00, 13, NULL, NULL);
INSERT INTO orders (customer_id, employee_id, shipper_id, freight) VALUES ('ALFKI', 1, 1, 32.38), ('ANATR', 2, 2, 11.61);
INSERT INTO order_details (order_id, product_id, unit_price, quantity) VALUES (1, 1, 18.00, 12), (1, 2, 19.00, 10), (2, 3, 10.00, 5);
REFRESH MATERIALIZED VIEW mv_customer_orders;
