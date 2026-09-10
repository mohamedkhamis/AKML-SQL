-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-07-case-short profile=default
SELECT
    o.orderid,
    CASE WHEN o.freight > 100 THEN 'high' ELSE 'low' END AS band
FROM   dbo.orders o;
