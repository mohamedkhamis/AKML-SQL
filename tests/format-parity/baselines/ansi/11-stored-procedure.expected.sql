-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=11-stored-procedure profile=ansi
create procedure dbo.GetCustomerOrders
    @customerid int,
    @startdate  datetime = null,
    @enddate    datetime = null
as
begin
    set    nocount on;
    if @startdate is null set    @startdate = '1900-01-01';
    if @enddate is null set    @enddate = GETDATE();
    select o.orderid, o.orderdate, o.total, c.customername
    from   orders o
    inner join   customers c
        on c.customerid = o.customerid
    where  o.customerid = @customerid
    and o.orderdate between @startdate and @enddate order by o.orderdate desc;
end;
