SELECT   department,
         STRING_AGG(name, ', ' ORDER BY name) AS names,
         COUNT(*) FILTER (WHERE active)
FROM     staff
GROUP BY department;
