-- 02-batch: a multi-statement batch with DECLARE/SET, INSERT, UPDATE, SELECT and GO separators.
declare @cutoff date = '2025-01-01';
declare @region nvarchar(50);
set @region = N'North';

insert into dbo.AuditLog (Action, Region, CreatedAt)
values ('batch-start', @region, sysutcdatetime());

update dbo.Orders
set Status = 'Reviewed'
where OrderDate < @cutoff
  and Status = 'Pending';
go

select count(*) as ReviewedCount
from dbo.Orders
where Status = 'Reviewed';
go
