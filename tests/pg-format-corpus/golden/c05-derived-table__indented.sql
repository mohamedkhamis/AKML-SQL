SELECT d.dept,
       d.n
FROM   (SELECT dept, COUNT(*) AS n FROM emp GROUP BY dept) AS d
WHERE  d.n > 3;
