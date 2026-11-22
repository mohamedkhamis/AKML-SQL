create procedure dbo.p @a int, @b varchar(10)='x' as begin set nocount on; select @a, @b end
go
create procedure dbo.p2 (@a int, @b nvarchar(max), @c decimal(10, 2) = 1.5) as select 1
go
create procedure dbo.p3 @d datetime = null, @e int output as set @e = 1
