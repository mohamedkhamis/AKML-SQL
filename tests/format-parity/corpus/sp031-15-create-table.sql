create table dbo.orderaudit (auditid int identity(1,1) not null constraint pk_orderaudit primary key, orderid int not null constraint fk_orderaudit_orders foreign key references dbo.orders (orderid), changedat datetime2(3) not null constraint df_orderaudit_changedat default sysutcdatetime(), oldfreight money null, newfreight money null, changedby nvarchar(128) not null);
create table dbo.regionmap (regionid int not null, territoryid nvarchar(20) not null, constraint pk_regionmap primary key (regionid, territoryid));
create table dbo.tinylookup (code char(2) not null primary key);
