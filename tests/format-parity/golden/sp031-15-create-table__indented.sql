CREATE TABLE dbo.orderaudit(
    auditid    int           identity (1, 1) NOT NULL constraint pk_orderaudit primary key,
    orderid    int           NOT NULL constraint fk_orderaudit_orders foreign key references dbo.orders(orderid),
    changedat  datetime2(3)  NOT NULL constraint df_orderaudit_changedat default SYSUTCDATETIME(),
    oldfreight money         NULL,
    newfreight money         NULL,
    changedby  nvarchar(128) NOT NULL
);

CREATE TABLE dbo.regionmap(
    regionid    int          NOT NULL,
    territoryid nvarchar(20) NOT NULL,

    constraint pk_regionmap primary key (regionid, territoryid)
);

CREATE TABLE dbo.tinylookup(
    code char(2) NOT NULL primary key
);
