-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=08-in-list-long profile=ansi
select productid, productname
from   products
where  category in(
    'Electronics',
    'Computers',
    'Phones',
    'Tablets',
    'Accessories',
    'Cables',
    'Adapters',
    'Chargers',
    'Headphones',
    'Speakers',
    'Cameras'
);
