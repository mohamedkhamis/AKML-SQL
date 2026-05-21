-- 04-cte: common table expressions, including a recursive CTE that walks
-- an employee/manager hierarchy, plus a second non-recursive CTE.
with DirectReports as
(
    select e.EmployeeId,
           e.ManagerId,
           e.FullName,
           0 as Depth
    from dbo.Employees as e
    where e.ManagerId is null

    union all

    select e.EmployeeId,
           e.ManagerId,
           e.FullName,
           dr.Depth + 1
    from dbo.Employees as e
    inner join DirectReports as dr on e.ManagerId = dr.EmployeeId
),
DepartmentSize as
(
    select e.DepartmentId,
           count(*) as HeadCount
    from dbo.Employees as e
    group by e.DepartmentId
)
select dr.EmployeeId,
       dr.FullName,
       dr.Depth,
       ds.HeadCount
from DirectReports as dr
left join DepartmentSize as ds on ds.DepartmentId = dr.EmployeeId
order by dr.Depth, dr.FullName;
