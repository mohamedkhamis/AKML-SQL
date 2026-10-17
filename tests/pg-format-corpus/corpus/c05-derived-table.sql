select d.dept, d.n from (select dept, count(*) as n from emp group by dept) as d where d.n > 3;
