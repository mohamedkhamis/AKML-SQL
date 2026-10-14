SELECT  id::TEXT
				 , price::NUMERIC( 10 , 2 )
				 , CAST( created AS DATE )
				 , '2024-01-01'::TIMESTAMP WITH TIME ZONE
				 , ( '1'::INT + 1 )::BIGINT
FROM    t ;
