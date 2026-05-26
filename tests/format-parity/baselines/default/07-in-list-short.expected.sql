-- akml-parity-baseline ide-build=1.26.0526.0000 corpus-item=07-in-list-short profile=default
SELECT *
FROM orders
WHERE status IN( 'Open', 'Pending', 'Shipped'
)
    AND customerid = 42;
