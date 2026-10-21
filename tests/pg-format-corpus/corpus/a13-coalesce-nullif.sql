select coalesce(nickname, first_name, 'anonymous'), nullif(discount, 0), greatest(a, b, c), least(a, b) from people;
