-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=06-case-simple profile=ansi
select
    productid, case
    status
    when
    'A'
    then
    'Active'
    when
    'D'
    then
    'Discontinued'
    when
    'P'
    then
    'Pending'
    else
    'Unknown'
    end
    as
    status_text
from products;
