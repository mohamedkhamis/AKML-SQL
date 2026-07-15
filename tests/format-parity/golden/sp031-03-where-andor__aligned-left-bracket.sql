SELECT e.employeeid, e.lastname
FROM   dbo.employees e
WHERE  e.country = 'USA'
AND e.title = 'Sales Representative'
OR e.reportsto IS NULL
AND e.hiredate >= '1993-01-01'
AND
(e.city = 'Seattle' OR e.city = 'Tacoma');
