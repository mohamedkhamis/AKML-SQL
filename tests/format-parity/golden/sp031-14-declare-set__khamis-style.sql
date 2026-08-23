DECLARE @startdate DATETIME = '1997-01-01', @enddate DATETIME = '1997-12-31', @country NVARCHAR ( 15 ) = N'Germany';

DECLARE @totalfreight MONEY;

SET    @totalfreight = (
  SELECT SUM ( o.freight ) FROM dbo.orders o
  WHERE  o.orderdate BETWEEN @startdate AND @enddate
  AND o.shipcountry = @country
);

SELECT @totalfreight AS totalfreight;
