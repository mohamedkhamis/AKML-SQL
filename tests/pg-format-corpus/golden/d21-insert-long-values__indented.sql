INSERT INTO sales.audit_log(event_time, user_name, action, details)
VALUES(NOW(), CURRENT_USER, 'Report', 'Customers by size and region for the regional sales team, sent weekly on Mondays'
),
      (NOW(), CURRENT_USER, 'Export', 'Orders for the last quarter, all regions, including cancelled orders and returns'
      );
