-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=sp047-01-proc-params profile=default
CREATE PROCEDURE dbo.p
    @a int,
    @b varchar(10) = 'x'
AS
BEGIN
    SET nocount ON;
    SELECT @a, @b
END

GO
CREATE PROCEDURE dbo.p2(
    @a int,
    @b nvarchar(MAX),
    @c decimal(10, 2) = 1.5
)
AS
SELECT 1

GO
CREATE PROCEDURE dbo.p3
    @d datetime = NULL,
    @e int output
AS
SET @e = 1
