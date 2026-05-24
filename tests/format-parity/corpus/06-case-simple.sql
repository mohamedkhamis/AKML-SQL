select productid,
case status
when 'A' then 'Active'
when 'D' then 'Discontinued'
when 'P' then 'Pending'
else 'Unknown'
end as status_text
from products;
