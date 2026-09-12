-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp031-03-where-andor profile=default
SELECT e.employeeid, e.lastname
FROM   dbo.employees e
WHERE  e.country = 'USA'
AND e.title = 'Sales Representative'
OR e.reportsto IS NULL
AND e.hiredate >= '1993-01-01'
AND (e.city = 'Seattle' OR e.city = 'Tacoma');
