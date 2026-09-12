-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-02-distinct-top profile=ansi
select distinct top 25 c.country, c.city from dbo.customers c order by c.country;

select top (100) percent p.productname, p.unitprice
from   dbo.products p
where  p.discontinued = 0;
