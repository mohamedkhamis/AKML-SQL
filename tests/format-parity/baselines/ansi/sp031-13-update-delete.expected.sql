-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-13-update-delete profile=ansi
update dbo.products
set    unitprice = unitprice * 1.1, reorderlevel = reorderlevel + 5
where  categoryid = 2 and discontinued = 0;

update p
set    p.unitsinstock = p.unitsinstock - od.quantity
from   dbo.products p
inner join   dbo.[order details] od
    on od.productid = p.productid
where  od.orderid = 11077;

delete from dbo.[order details]
where  orderid = 10248 and productid in(
    11,
    42,
    72
);
