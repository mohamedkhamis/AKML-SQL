delete from logs where created < now() - interval '90 days';
