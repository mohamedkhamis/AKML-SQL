SELECT
    o.orderid AS id
    , o.orderdate
    , o.requireddate
    , o.shippeddate
    , o.shipvia
    , o.freight
    , o.shipname
    , o.shipaddress
    , o.shipcity
    , o.shipregion
    , o.shippostalcode
    , o.shipcountry
FROM   dbo.orders o
WHERE  o.freight > 50
ORDER BY o.orderdate DESC, o.orderid;
