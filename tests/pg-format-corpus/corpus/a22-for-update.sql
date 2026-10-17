select * from jobs where state = 'new' order by id limit 1 for update skip locked;
