-- 01-select: a representative SELECT with an INNER JOIN, a WHERE filter and ORDER BY.
select c.CustomerId,
       c.CompanyName,
       o.OrderId,
       o.OrderDate,
       o.TotalAmount
from dbo.Customers as c
inner join dbo.Orders as o on o.CustomerId = c.CustomerId
where o.OrderDate >= '2025-01-01'
  and o.TotalAmount > 100.00
order by o.OrderDate desc, c.CompanyName asc;
