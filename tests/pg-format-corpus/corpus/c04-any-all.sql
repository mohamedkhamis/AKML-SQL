select * from t where x > any (select y from u) and x <= all (select z from w);
