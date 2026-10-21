CREATE TABLE products(id          serial                   PRIMARY KEY,
                      name        text                     NOT NULL,
                      price       numeric(10, 2)           DEFAULT 0 CHECK (price >= 0),
                      category_id integer                  REFERENCES categories (id) ON DELETE SET NULL,
                      created     timestamp with time zone DEFAULT NOW());
