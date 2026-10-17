MERGE INTO stock s
USING deliveries d
ON s.item = d.item
WHEN MATCHED THEN
    UPDATE SET qty = s.qty + d.qty
WHEN NOT MATCHED THEN
    INSERT (item, qty) VALUES (d.item, d.qty);
