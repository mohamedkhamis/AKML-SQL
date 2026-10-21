create materialized view sales_by_month as select date_trunc('month', d) m, sum(x) from sales group by 1 with no data;
