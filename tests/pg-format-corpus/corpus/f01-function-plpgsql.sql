create or replace function add_one(i integer) returns integer as $$
begin
    return i + 1;
end;
$$ language plpgsql immutable;
