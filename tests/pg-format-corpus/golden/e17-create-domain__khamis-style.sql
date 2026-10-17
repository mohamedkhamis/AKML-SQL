CREATE DOMAIN us_zip AS text CHECK (value ~ '^\d{5}$') ;
