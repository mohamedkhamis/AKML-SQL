SELECT * FROM orders WHERE total >= 100 AND total <> 250 AND status != 'void' OR priority < 3 AND NOT archived;
