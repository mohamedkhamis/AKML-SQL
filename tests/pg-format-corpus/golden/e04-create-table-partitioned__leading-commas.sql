CREATE TABLE measurements(ts     timestamptz NOT NULL
                        , device int
                        , value  double precision) PARTITION BY range (ts);
