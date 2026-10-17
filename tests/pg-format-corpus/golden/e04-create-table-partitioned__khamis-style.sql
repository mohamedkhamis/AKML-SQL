CREATE TABLE measurements (
	ts     TIMESTAMPTZ
		NOT NULL
	 , device INT
	 , value  DOUBLE PRECISION
) PARTITION BY range ( ts ) ;
