CREATE TRIGGER trg_audit after INSERT OR UPDATE ON products FOR each ROW WHEN (old.* IS DISTINCT FROM new.*) EXECUTE FUNCTION log_it();
