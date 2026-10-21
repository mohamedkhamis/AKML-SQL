select department, string_agg(name, ', ' order by name) as names, count(*) filter (where active) from staff group by department;
