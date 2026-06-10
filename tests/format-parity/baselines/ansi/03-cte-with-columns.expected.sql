-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=03-cte-with-columns profile=ansi
with monthly_sales(year_num, month_num, region, total_amount
) as (
    select YEAR(orderdate), MONTH(orderdate), region, SUM(amount)
    from   orders
    group by YEAR(orderdate), MONTH(orderdate), region
    ) select year_num, month_num, region, total_amount
from   monthly_sales
where  total_amount > 1000;
