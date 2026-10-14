select * from orders
where total >= 100 and total <> 250 and status != 'void' or priority < 3 and not archived;
