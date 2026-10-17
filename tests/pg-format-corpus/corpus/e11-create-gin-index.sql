create index idx_tags on docs using gin (tags jsonb_path_ops) with (fastupdate = off);
