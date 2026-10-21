update orders o set total = s.sum from (select order_id, sum(price * qty) as sum from lines group by order_id) s where s.order_id = o.id returning o.id, o.total;
