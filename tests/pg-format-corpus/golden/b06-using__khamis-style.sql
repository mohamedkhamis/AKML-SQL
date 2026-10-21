SELECT  *
FROM    a
				JOIN b USING ( id )
				JOIN c USING ( id, kind ) ;
