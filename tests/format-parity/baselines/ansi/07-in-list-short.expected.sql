-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=07-in-list-short profile=ansi
select *
from orders
where status in( 'Open', 'Pending', 'Shipped'
)
    and customerid = 42;
