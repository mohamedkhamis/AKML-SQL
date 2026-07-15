select distinct top 25 c.country, c.city from dbo.customers c order by c.country;
select top (100) percent p.productname, p.unitprice from dbo.products p where p.discontinued = 0;
