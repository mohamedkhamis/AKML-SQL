-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=01-simple-select profile=default
SELECT
    customerid, customername, country
FROM customers
WHERE country = 'USA'
ORDER BY customername;
