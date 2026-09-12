-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-03-where-andor profile=ansi
select e.employeeid, e.lastname
from   dbo.employees e
where  e.country = 'USA'
and e.title = 'Sales Representative'
or e.reportsto is null
and e.hiredate >= '1993-01-01'
and (e.city = 'Seattle' or e.city = 'Tacoma');
