-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=10-ddl-create-table profile=ansi
create table dbo.orders (
orderid int identity(1, 1) not null primary key,
customerid int not null,
orderdate datetime not null default(getdate()),
total decimal(18, 2) not null,
status varchar(20) not null,
constraint fk_orders_customers foreign key (customerid) references dbo.customers(customerid),
constraint ck_orders_total check (total >= 0)
);
