SELECT o.orderid, c.companyname, e.lastname, s.companyname AS shipper
FROM   dbo.orders o
INNER JOIN   dbo.customers c
    ON c.customerid = o.customerid
LEFT OUTER JOIN   dbo.employees e
    ON e.employeeid = o.employeeid
INNER JOIN   dbo.shippers s
    ON s.shipperid = o.shipvia
WHERE  o.shipcountry = 'Mexico';
