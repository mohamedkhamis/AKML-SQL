CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS idx_u
ON t USING btree (LOWER(email) text_pattern_ops, created DESC NULLS LAST)
INCLUDE (name)
WHERE deleted_at IS NULL;
