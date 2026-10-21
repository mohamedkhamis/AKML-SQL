prepare q (int) as select * from products where id = $1; execute q(1); deallocate q;
