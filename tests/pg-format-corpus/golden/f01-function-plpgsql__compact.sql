CREATE OR REPLACE FUNCTION add_one(i integer) RETURNS integer AS $$
begin
    return i + 1;
end;
$$ LANGUAGE plpgsql IMMUTABLE;
