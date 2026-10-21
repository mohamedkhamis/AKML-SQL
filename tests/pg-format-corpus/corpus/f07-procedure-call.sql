create procedure archive_old(cutoff date) language sql as $$ delete from logs where created < cutoff $$; call archive_old('2024-01-01');
