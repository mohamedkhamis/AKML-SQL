CREATE TABLE products (
	id          SERIAL
		PRIMARY KEY
	 , name        TEXT
			 NOT NULL
	 , price       NUMERIC( 10 , 2 )
			 DEFAULT 0 CHECK ( price >= 0 )
	 , category_id INTEGER
			 REFERENCES categories ( id ) ON DELETE SET NULL
	 , created     TIMESTAMP WITH TIME ZONE
			 DEFAULT NOW ( )
) ;
