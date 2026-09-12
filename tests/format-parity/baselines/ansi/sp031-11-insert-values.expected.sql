-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-11-insert-values profile=ansi
insert into dbo.shippers(companyname, phone)
values      (N'Alpha Freight', N'(503) 555-0100');

insert into dbo.shippers(companyname, phone)
values      (N'Beta Cargo', N'(503) 555-0101'),(N'Gamma Lines', N'(503) 555-0102'),(N'Delta Express',
    N'(503) 555-0103');
