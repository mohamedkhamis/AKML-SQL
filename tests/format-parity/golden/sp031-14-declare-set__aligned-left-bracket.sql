DECLARE @startdate datetime = '1997-01-01', @enddate datetime = '1997-12-31', @country nvarchar(15) = N'Germany';

DECLARE @totalfreight money;

SET    @totalfreight =
(
    SELECT SUM(o.freight) FROM dbo.orders o
    WHERE  o.orderdate BETWEEN @startdate AND @enddate
    AND o.shipcountry = @country
);

SELECT @totalfreight AS totalfreight;
