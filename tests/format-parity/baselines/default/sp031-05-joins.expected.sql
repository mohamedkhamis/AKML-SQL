-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-05-joins profile=default
SELECT o.orderid, c.companyname, e.lastname, s.companyname AS shipper
FROM   dbo.orders o
INNER JOIN   dbo.customers c
    ON c.customerid = o.customerid
LEFT OUTER JOIN   dbo.employees e
    ON e.employeeid = o.employeeid
INNER JOIN   dbo.shippers s
    ON s.shipperid = o.shipvia
WHERE  o.shipcountry = 'Mexico';
