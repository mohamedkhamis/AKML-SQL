select id, case when total > 1000 then 'large' when total > 100 then 'medium' else 'small' end as size from orders;
