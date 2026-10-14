select * from t where a = $1 and b = any($2) and c between $3 and $4 limit $5;
