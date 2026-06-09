-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=10-ddl-create-table profile=default
CREATE TABLE dbo.orders(
    orderid    int           identity (1,
    1) NOT NULL primary key,
    customerid int           NOT NULL,
    orderdate  datetime      NOT NULL default (GETDATE()),
    total      decimal(18,
    2) NOT NULL,
    status     varchar(20)   NOT NULL,

    constraint fk_orders_customers foreign key (customerid) references dbo.customers(customerid),

    constraint ck_orders_total check (total > = 0)
);
