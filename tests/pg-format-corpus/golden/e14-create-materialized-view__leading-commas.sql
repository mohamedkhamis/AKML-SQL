CREATE MATERIALIZED VIEW sales_by_month
AS
SELECT DATE_TRUNC('month', d) m, SUM(x) FROM sales GROUP BY 1 WITH no data;
