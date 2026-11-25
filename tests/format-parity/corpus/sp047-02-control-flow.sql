create procedure dbo.cf @a int as
begin
if @a > 1 begin select 1 end else begin select 2 end;
if @a = 0 select 0 else if @a = 1 begin select 1 end;
while @a > 0 begin set @a = @a - 1; if @a = 5 begin break end end
begin try begin select 1/0 end end try begin catch select error_message() end catch
if @a > 2
begin
select case when @a = 3 then 1 else 0 end
select 4
end
else
begin
select 5
end
while @a > 9
begin
set @a = @a - 1
end
if @a = 6 select 6 else if @a = 7 select 7 else select 8
begin tran
set @a = 0
commit
end
