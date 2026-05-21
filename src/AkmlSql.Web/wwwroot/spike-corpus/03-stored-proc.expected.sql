-- 03-stored-proc: an order-processing procedure exercising parameters,
-- control flow (IF / ELSE / BEGIN / END), a transaction, and TRY/CATCH.
-- Deliberately > 50 lines to exercise the formatter pipeline end-to-end.
create procedure dbo.usp_ProcessCustomerOrder
    @CustomerId    int,
    @OrderId       int,
    @ApplyDiscount bit           = 0,
    @DiscountPct   decimal(5, 2) = 0.00,
    @ResultMessage nvarchar(200) = null output
as
begin
    set nocount on;

    declare @OrderTotal   decimal(18, 2);
    declare @CustomerTier varchar(20);
    declare @IsActive     bit;

    -- Validate the customer exists and is active.
    select @IsActive = c.IsActive,
           @CustomerTier = c.Tier
    from dbo.Customers as c
    where c.CustomerId = @CustomerId;

    if @IsActive is null
    begin
        set @ResultMessage = 'Customer not found.';
        return 1;
    end

    if @IsActive = 0
    begin
        set @ResultMessage = 'Customer is inactive.';
        return 2;
    end

    -- Total the order line items.
    select @OrderTotal = sum(oi.Quantity * oi.UnitPrice)
    from dbo.OrderItems as oi
    where oi.OrderId = @OrderId;

    if @OrderTotal is null
    begin
        set @ResultMessage = 'Order has no line items.';
        return 3;
    end

    -- Apply a tier-based or explicit discount.
    if @ApplyDiscount = 1
    begin
        if @CustomerTier = 'Gold'
            set @DiscountPct = @DiscountPct + 10.00;
        else if @CustomerTier = 'Silver'
            set @DiscountPct = @DiscountPct + 5.00;

        set @OrderTotal = @OrderTotal * (1.0 - @DiscountPct / 100.0);
    end

    begin try
        begin transaction;

        update dbo.Orders
        set TotalAmount = @OrderTotal,
            Status      = 'Processed',
            ProcessedAt = sysutcdatetime()
        where OrderId = @OrderId;

        insert into dbo.AuditLog (Action, OrderId, CreatedAt)
        values ('order-processed', @OrderId, sysutcdatetime());

        commit transaction;
        set @ResultMessage = 'Order processed successfully.';
    end try
    begin catch
        if @@trancount > 0
            rollback transaction;

        set @ResultMessage = 'Processing failed: ' + error_message();
        return 99;
    end catch

    return 0;
end
go
