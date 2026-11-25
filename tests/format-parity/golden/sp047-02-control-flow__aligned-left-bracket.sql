CREATE PROCEDURE dbo.cf
    @a int
AS
BEGIN
    IF @a > 1
    BEGIN
        SELECT 1
    END
    ELSE
    BEGIN
        SELECT 2
    END;
    IF @a = 0
    SELECT 0
    ELSE IF @a = 1
    BEGIN
        SELECT 1
    END;
    WHILE @a > 0
    BEGIN
        SET @a = @a - 1;
        IF @a = 5
        BEGIN
            BREAK
        END
    END
    BEGIN try
        BEGIN
            SELECT 1 / 0
        END
    END try
    BEGIN catch
        SELECT ERROR_MESSAGE()
    END catch
    IF @a > 2
    BEGIN
        SELECT CASE WHEN @a = 3 THEN 1 ELSE 0 END
        SELECT 4
    END
    ELSE
    BEGIN
        SELECT 5
    END
    WHILE @a > 9
    BEGIN
        SET @a = @a - 1
    END
    IF @a = 6
    SELECT 6
    ELSE IF @a = 7
        SELECT 7
    ELSE SELECT 8
    BEGIN tran
    SET @a = 0
    COMMIT
END
