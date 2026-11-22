CREATE PROCEDURE dbo.p
    @a INT,
    @b VARCHAR (10) = 'x'
AS
BEGIN
    SET nocount ON;
    SELECT @a, @b
END

GO
CREATE PROCEDURE dbo.p2 ( @a INT, @b NVARCHAR (MAX), @c DECIMAL (10, 2) = 1.5) AS SELECT 1

GO
CREATE PROCEDURE dbo.p3 @d DATETIME = NULL, @e INT output AS SET @e = 1
