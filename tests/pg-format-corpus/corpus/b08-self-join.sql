select e.name, m.name from employees e left join employees m on m.id = e.manager_id;
