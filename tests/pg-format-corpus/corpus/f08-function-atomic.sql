create function twice(x int) returns int language sql immutable parallel safe begin atomic select x * 2; end;
