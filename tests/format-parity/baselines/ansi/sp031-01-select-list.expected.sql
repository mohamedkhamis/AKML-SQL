-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-01-select-list profile=ansi
select
    o.orderid as id,
    o.orderdate,
    o.requireddate,
    o.shippeddate,
    o.shipvia,
    o.freight,
    o.shipname,
    o.shipaddress,
    o.shipcity,
    o.shipregion,
    o.shippostalcode,
    o.shipcountry
from   dbo.orders o
where  o.freight > 50
order by o.orderdate desc, o.orderid;
