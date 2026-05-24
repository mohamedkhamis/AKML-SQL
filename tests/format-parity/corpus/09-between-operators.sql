select orderid, total
from orders
where orderdate between '2025-01-01' and '2025-12-31'
and total between 100 and 10000
and (status = 'Open' or status = 'Pending');
