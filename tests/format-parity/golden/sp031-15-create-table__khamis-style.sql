CREATE TABLE dbo.orderaudit (
  auditid    INT
    identity ( 1, 1 ) NOT NULL constraint pk_orderaudit primary key,
  orderid    INT
    NOT NULL constraint fk_orderaudit_orders foreign key references dbo.orders ( orderid ),
  changedat  DATETIME2 ( 3 )
    NOT NULL constraint df_orderaudit_changedat default SYSUTCDATETIME ( ),
  oldfreight MONEY
    NULL,
  newfreight MONEY
    NULL,
  changedby  NVARCHAR ( 128 )
    NOT NULL
);

CREATE TABLE dbo.regionmap (
  regionid    INT
    NOT NULL,
  territoryid NVARCHAR ( 20 )
    NOT NULL,

    constraint pk_regionmap primary key ( regionid, territoryid )
);

CREATE TABLE dbo.tinylookup ( code CHAR ( 2 ) NOT NULL primary key );
