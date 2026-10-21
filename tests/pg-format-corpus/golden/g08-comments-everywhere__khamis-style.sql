-- the first query
SELECT    id -- the key
					 , name /* the name */
					 , price
FROM      products -- source
WHERE     price > 0 -- only priced
/* second */
-- and a note
ORDER BY  id ;
