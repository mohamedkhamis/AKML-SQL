CREATE ROLE app LOGIN PASSWORD 'x' VALID UNTIL 'infinity' ;


ALTER ROLE app SET search_path = public, audit ;
