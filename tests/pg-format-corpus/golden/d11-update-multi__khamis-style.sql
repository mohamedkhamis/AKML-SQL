UPDATE  employees
SET     salary = salary + 100
				 , title = 'Senior'
				 , updated_at = NOW ( )
WHERE   id = 7
				AND active ;
