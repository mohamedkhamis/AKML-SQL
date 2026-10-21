CREATE TABLE order_lines(order_id   int NOT NULL
                       , line       int NOT NULL
                       , product_id int NOT NULL
                       , qty        int NOT NULL DEFAULT 1
                       , PRIMARY KEY (order_id, line)
                       , FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE CASCADE
                       , CONSTRAINT qty_pos CHECK (qty > 0)
                       , UNIQUE (order_id, product_id));
