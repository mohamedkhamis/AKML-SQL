-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-08-case-long profile=default
SELECT
    o.orderid,
    CASE
        WHEN o.freight > 500
    AND o.shipcountry NOT IN(
    'USA',
    'Canada'
) THEN 'international heavy'
        WHEN o.freight > 100
    THEN 'heavy shipment overweight'
        WHEN o.freight > 50
    AND o.shipvia = 3
    THEN 'medium express shipment'
        ELSE 'standard ground delivery'
        END AS freightband,
    CASE o.shipvia
        WHEN 1
    THEN 'speedy'
        WHEN 2
    THEN 'united'
        ELSE 'federal'
        END AS shippername
FROM   dbo.orders o;
