-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=06-case-simple profile=default
SELECT
    productid, CASE
    status
    WHEN
    'A'
    THEN
    'Active'
    WHEN
    'D'
    THEN
    'Discontinued'
    WHEN
    'P'
    THEN
    'Pending'
    ELSE
    'Unknown'
    END
    AS
    status_text
FROM products;
