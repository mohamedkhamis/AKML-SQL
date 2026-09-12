-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-16-control-flow profile=ansi
if @@rowcount = 0 print 'none';

if exists(
    select 1 from dbo.orders o
    where  o.shippeddate is null and o.requireddate < GETDATE()
)
begin
    update dbo.orders
    set    shipvia = 3
    where  shippeddate is null and requireddate < GETDATE();
    print 'expedited late orders';
end
else
begin
    print 'no late orders';
end

while (
    select COUNT(*) from dbo.products
    where  unitsinstock = 0
) > 0
begin
    update top (10) dbo.products
    set    unitsinstock = reorderlevel
    where  unitsinstock = 0;
    if @@rowcount = 0 break;
end
