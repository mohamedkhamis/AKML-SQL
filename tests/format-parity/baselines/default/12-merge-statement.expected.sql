-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=12-merge-statement profile=default
merge into dbo.targettable as t
using dbo.sourcetable as s on t.id = s.id
when matched and t.value <> s.value then update set t.value = s.value, t.modified = getdate()
when not matched by target then insert (id, value, created) values (s.id, s.value, getdate())
when not matched by source then delete;
