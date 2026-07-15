select o.orderid, case when o.freight > 100 then 'high' else 'low' end as band from dbo.orders o;
