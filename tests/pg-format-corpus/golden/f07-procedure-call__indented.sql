CREATE PROCEDURE archive_old(cutoff date) LANGUAGE sql AS $$ delete from logs where created < cutoff $$;


CALL archive_old('2024-01-01');
