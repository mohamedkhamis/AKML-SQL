-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-07-case-short profile=ansi
select
    o.orderid,
    case when o.freight > 100 then 'high' else 'low' end as band
from   dbo.orders o;
