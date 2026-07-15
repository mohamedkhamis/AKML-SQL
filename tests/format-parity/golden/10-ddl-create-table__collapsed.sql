CREATE TABLE dbo.orders (
    orderid    INT             identity (1, 1) NOT NULL primary key,
    customerid INT             NOT NULL,
    orderdate  DATETIME        NOT NULL default (GETDATE ()),
    total      DECIMAL (18, 2) NOT NULL,
    status     VARCHAR (20)    NOT NULL,

    constraint fk_orders_customers foreign key (customerid) references dbo.customers (customerid),

    constraint ck_orders_total check (total >= 0)
);
