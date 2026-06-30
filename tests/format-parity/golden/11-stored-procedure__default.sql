CREATE PROCEDURE dbo.GetCustomerOrders
    @customerid int,
    @startdate  datetime = NULL,
    @enddate    datetime = NULL
AS
BEGIN
    SET    nocount ON;
    IF @startdate IS NULL SET @startdate = '1900-01-01';
    IF @enddate IS NULL SET @enddate = GETDATE();
    SELECT o.orderid, o.orderdate, o.total, c.customername
    FROM   orders o
    INNER JOIN   customers c
        ON c.customerid = o.customerid
    WHERE  o.customerid = @customerid
    AND o.orderdate BETWEEN @startdate AND @enddate ORDER BY o.orderdate DESC;
END;
