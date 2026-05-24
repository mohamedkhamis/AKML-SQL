with monthly_sales (year_num, month_num, region, total_amount) as (
    select year(orderdate), month(orderdate), region, sum(amount)
    from orders
    group by year(orderdate), month(orderdate), region
)
select year_num, month_num, region, total_amount
from monthly_sales
where total_amount > 1000;
