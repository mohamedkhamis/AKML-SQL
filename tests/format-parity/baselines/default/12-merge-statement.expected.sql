-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=12-merge-statement profile=default
merge INTO dbo.targettable AS t using dbo.sourcetable AS s ON t.id = s.id
WHEN matched
AND t.value < > s.value THEN UPDATE
SET    t.value = s.value,
    t.modified = GETDATE()
    WHEN NOT matched BY target THEN INSERT (id, value, created)
VALUES (s.id, s.value, GETDATE()) WHEN NOT matched BY source THEN DELETE ;
