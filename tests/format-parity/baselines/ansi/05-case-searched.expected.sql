-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=05-case-searched profile=ansi
select
    orderid, total, case
    when
    total >
    1000
    then
    'Large'
    when
    total >
    100
    then
    'Medium'
    when
    total >
    0
    then
    'Small'
    else
    'Empty'
    end
    as
    size_bucket
from orders;
