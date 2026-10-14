select * from customers where phone is null and fax is not null and region is distinct from 'EU' and city is not distinct from 'Paris';
