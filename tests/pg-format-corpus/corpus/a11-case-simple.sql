select case status when 'N' then 'new' when 'P' then 'paid' else 'other' end from orders;
