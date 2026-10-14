DELETE FROM logs WHERE created < NOW() - interval '90 days';
