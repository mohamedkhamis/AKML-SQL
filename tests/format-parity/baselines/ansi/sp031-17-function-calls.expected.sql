-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-17-function-calls profile=ansi
select
    GETDATE()                                as now,
    ISNULL(o.shippeddate, o.requireddate)    as effectivedate,
    DATEDIFF(DAY,
    o.orderdate,
    ISNULL(o.shippeddate, GETDATE())
    )                                        as daystoship,
    UPPER(SUBSTRING(c.companyname,
    1,
    CHARINDEX(' ', c.companyname + ' ') - 1
    )
    )                                        as firstword,
    COALESCE(o.shipregion, c.region, N'n/a') as region
from   dbo.orders o
inner join   dbo.customers c
    on c.customerid = o.customerid;
