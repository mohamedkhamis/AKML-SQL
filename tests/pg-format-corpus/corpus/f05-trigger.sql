create trigger trg_audit after insert or update on products for each row when (old.* is distinct from new.*) execute function log_it();
