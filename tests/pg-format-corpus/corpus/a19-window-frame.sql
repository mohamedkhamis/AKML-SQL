select d, sum(x) over (order by d rows between 2 preceding and current row), avg(x) over (order by d range between unbounded preceding and unbounded following) from series;
