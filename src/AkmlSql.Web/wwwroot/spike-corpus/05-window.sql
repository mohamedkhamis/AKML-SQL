-- 05-window: window functions over a sales dataset -- ROW_NUMBER, SUM() OVER
-- with an explicit frame, and LAG, each with PARTITION BY / ORDER BY.
select s.SalesPersonId,
       s.SaleDate,
       s.Amount,
       row_number() over (partition by s.SalesPersonId order by s.SaleDate) as SaleSeq,
       sum(s.Amount) over (partition by s.SalesPersonId order by s.SaleDate
           rows between unbounded preceding and current row) as RunningTotal,
       lag(s.Amount, 1, 0) over (partition by s.SalesPersonId order by s.SaleDate) as PrevAmount,
       s.Amount - lag(s.Amount, 1, 0) over (partition by s.SalesPersonId order by s.SaleDate) as DeltaFromPrev
from dbo.Sales as s
where s.SaleDate >= '2025-01-01'
order by s.SalesPersonId, s.SaleDate;
