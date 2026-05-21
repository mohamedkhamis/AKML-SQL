-- 06-merge: a MERGE statement that upserts staged rows into a target
-- dimension, with WHEN MATCHED, WHEN NOT MATCHED BY TARGET / BY SOURCE.
merge dbo.DimProduct as target
using dbo.StagingProduct as source
    on target.ProductKey = source.ProductKey
when matched and (target.ProductName <> source.ProductName
                  or target.ListPrice <> source.ListPrice) then
    update set target.ProductName = source.ProductName,
               target.ListPrice = source.ListPrice,
               target.UpdatedAt = sysutcdatetime()
when not matched by target then
    insert (ProductKey, ProductName, ListPrice, CreatedAt)
    values (source.ProductKey, source.ProductName, source.ListPrice, sysutcdatetime())
when not matched by source then
    delete
output $action, inserted.ProductKey, deleted.ProductKey;
