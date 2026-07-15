IF @@rowcount = 0 PRINT 'none'; IF EXISTS(
    SELECT 1 FROM dbo.orders o
    WHERE  o.shippeddate IS NULL AND o.requireddate < GETDATE()
)
BEGIN
    UPDATE dbo.orders SET shipvia = 3 WHERE shippeddate IS NULL AND requireddate < GETDATE();
    PRINT 'expedited late orders';
END
ELSE
BEGIN
    PRINT 'no late orders';
END WHILE (
    SELECT COUNT(*) FROM dbo.products
    WHERE  unitsinstock = 0
) > 0
BEGIN
    UPDATE TOP (10) dbo.products SET unitsinstock = reorderlevel WHERE unitsinstock = 0;
    IF @@rowcount = 0 BREAK;
END
