-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-12-insert-select profile=ansi
insert into dbo.customerarchive(customerid, companyname, contactname, country, archivedate
) select
    c.customerid,
    c.companyname,
    c.contactname,
    c.country,
    GETDATE()
from        dbo.customers c
where       not exists(
    select 1
    from        dbo.orders o
    where       o.customerid = c.customerid and o.orderdate >= '1997-01-01'
);
