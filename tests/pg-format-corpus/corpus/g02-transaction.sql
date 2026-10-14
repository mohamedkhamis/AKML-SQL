begin; update a set x = 1; savepoint s1; update a set x = 2; rollback to savepoint s1; commit;
