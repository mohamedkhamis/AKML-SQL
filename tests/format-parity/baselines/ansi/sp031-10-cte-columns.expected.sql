-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-10-cte-columns profile=ansi
with ordertotals(orderid, customerid, linecount, totalvalue
) as (
    select od.orderid,
    o.customerid,
    COUNT(*),
    SUM(od.unitprice * od.quantity * (1 - od.discount)
    )
    from   dbo.[order details] od inner join dbo.orders o on o.orderid = od.orderid
    group by od.orderid, o.customerid
    ),

    customerranks(customerid, RANK) as (
    select customerid, ROW_NUMBER() over ( order by SUM(totalvalue) desc)
    from   ordertotals
    group by customerid
    )
select cr.customerid, cr.RANK, ot.totalvalue
from   customerranks cr
inner join   ordertotals ot
    on ot.customerid = cr.customerid
where  cr.RANK <= 10;
