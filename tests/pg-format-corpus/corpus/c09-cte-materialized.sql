with m as materialized (select * from big where flag), n as not materialized (select * from big) select * from m join n using (id);
