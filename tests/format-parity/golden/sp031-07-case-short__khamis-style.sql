SELECT o.orderid
  , CASE WHEN o.freight > 100 THEN 'high' ELSE 'low' END AS band FROM dbo.orders o;
