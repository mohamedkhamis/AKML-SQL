declare @startdate datetime = '1997-01-01', @enddate datetime = '1997-12-31', @country nvarchar(15) = N'Germany';
declare @totalfreight money;
set @totalfreight = (select sum(o.freight) from dbo.orders o where o.orderdate between @startdate and @enddate and o.shipcountry = @country);
select @totalfreight as totalfreight;
