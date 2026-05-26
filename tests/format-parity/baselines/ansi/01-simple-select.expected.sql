-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=01-simple-select profile=ansi
select
    customerid, customername, country
from customers
where country = 'USA'
order by customername;
