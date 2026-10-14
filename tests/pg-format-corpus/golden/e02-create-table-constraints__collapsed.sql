CREATE TABLE order_lines (order_id   INT NOT NULL,
                          line       INT NOT NULL,
                          product_id INT NOT NULL,
                          qty        INT NOT NULL DEFAULT 1,
                          PRIMARY KEY (order_id, line),
                          FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE CASCADE,
                          CONSTRAINT qty_pos CHECK (qty > 0),
                          UNIQUE (order_id, product_id));
