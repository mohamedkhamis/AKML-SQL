create unique index concurrently if not exists idx_u on t using btree (lower(email) text_pattern_ops, created desc nulls last) include (name) where deleted_at is null;
