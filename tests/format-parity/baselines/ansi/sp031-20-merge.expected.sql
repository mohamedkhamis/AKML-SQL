-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-20-merge profile=ansi
merge dbo.products as target using(
    select productid,
    SUM(quantity) as sold from dbo.[order details]
    group by productid
    ) as source on target.productid = source.productid
when matched
    and target.unitsinstock >= source.sold then update
set    target.unitsinstock = target.unitsinstock - source.sold
when matched then update
set    target.unitsinstock = 0
when not matched by target then insert (productname, unitsinstock) values (N'unknown', 0);
