SELECT orderid, total FROM orders WHERE orderdate
    BETWEEN '2025-01-01' AND '2025-12-31' AND total
    BETWEEN 100 AND 10000 AND (status = 'Open' OR status = 'Pending');
