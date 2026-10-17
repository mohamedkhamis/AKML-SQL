select id, row_number() over (partition by dept order by salary desc) rn, lag(salary) over w, sum(salary) over (partition by dept) from emp window w as (order by id);
