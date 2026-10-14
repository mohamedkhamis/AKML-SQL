CREATE INDEX idx_tags
ON docs USING gin ( tags jsonb_path_ops )
WITH ( fastupdate = off ) ;
