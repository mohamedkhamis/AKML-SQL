MERGE dbo.products AS target USING(
    SELECT productid,
    SUM(quantity) AS sold FROM dbo.[order details]
    GROUP BY productid
) AS source ON target.productid = source.productid
WHEN MATCHED
    AND target.unitsinstock >= source.sold THEN UPDATE
SET    target.unitsinstock = target.unitsinstock - source.sold
WHEN MATCHED THEN UPDATE
SET    target.unitsinstock = 0
WHEN NOT MATCHED BY TARGET THEN INSERT
(productname, unitsinstock) VALUES
(N'unknown', 0);
