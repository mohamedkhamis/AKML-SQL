CREATE OR REPLACE VIEW active_customers
AS
SELECT  id
				 , name
FROM    customers
WHERE   active ;
