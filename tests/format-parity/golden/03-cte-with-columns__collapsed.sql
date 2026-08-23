WITH monthly_sales (year_num, month_num, region, total_amount)
AS (
    SELECT YEAR (orderdate),
    MONTH (orderdate),
    region,
    SUM (amount) FROM orders GROUP BY YEAR (orderdate), MONTH (orderdate), region)
SELECT year_num, month_num, region, total_amount FROM monthly_sales WHERE total_amount > 1000;
