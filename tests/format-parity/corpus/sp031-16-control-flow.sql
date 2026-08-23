if @@rowcount = 0 print 'none';
if exists (select 1 from dbo.orders o where o.shippeddate is null and o.requireddate < getdate()) begin update dbo.orders set shipvia = 3 where shippeddate is null and requireddate < getdate(); print 'expedited late orders'; end else begin print 'no late orders'; end
while (select count(*) from dbo.products where unitsinstock = 0) > 0 begin update top (10) dbo.products set unitsinstock = reorderlevel where unitsinstock = 0; if @@rowcount = 0 break; end
