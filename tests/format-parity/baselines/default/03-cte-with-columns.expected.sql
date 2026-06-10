-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=03-cte-with-columns profile=default
WITH monthly_sales(year_num, month_num, region, total_amount
) AS (
    SELECT YEAR(orderdate), MONTH(orderdate), region, SUM(amount)
    FROM   orders
    GROUP BY YEAR(orderdate), MONTH(orderdate), region
    ) SELECT year_num, month_num, region, total_amount
FROM   monthly_sales
WHERE  total_amount > 1000;
