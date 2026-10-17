SELECT  a::INT::TEXT
				 , ( a )::TEXT
				 , a::CHARACTER VARYING( 10 )
				 , a::DOUBLE PRECISION
				 , a::INT[]
				 , a::public.mood
				 , a::"Mixed Type"
FROM    t ;
