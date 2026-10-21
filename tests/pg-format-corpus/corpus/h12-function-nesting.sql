select coalesce(nullif(trim(both ' ' from upper(name)), ''), 'n/a'), substring(s from 2 for 3), position('a' in s), extract(year from d), overlay(s placing 'x' from 2) from t;
