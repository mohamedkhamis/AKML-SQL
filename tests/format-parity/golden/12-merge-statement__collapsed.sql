MERGE INTO dbo.targettable AS t USING dbo.sourcetable AS s ON t.id = s.id
WHEN MATCHED
AND t.value <> s.value THEN UPDATE SET t.value = s.value,
    t.modified = GETDATE ()
WHEN NOT MATCHED BY TARGET THEN INSERT (id, value, created) VALUES (s.id, s.value, GETDATE ())
WHEN NOT MATCHED BY SOURCE THEN DELETE;
