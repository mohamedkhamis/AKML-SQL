select case when a > 0 then case when b > 0 then 'both' else 'a only' end else 'none' end as r from t;
