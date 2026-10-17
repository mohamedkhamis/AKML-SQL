SELECT e.name, m.name FROM employees e LEFT JOIN employees m ON m.id = e.manager_id;
