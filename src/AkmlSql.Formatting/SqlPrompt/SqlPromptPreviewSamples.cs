namespace AkmlSql.Formatting.SqlPrompt;

/// <summary>
/// The preview SQL for each page of the style editor — like SQL Prompt's editor, each page
/// previews code its options act on, so changing an option visibly changes the preview.
/// </summary>
public static class SqlPromptPreviewSamples
{
    public static string For(string sectionId) => Samples.GetValueOrDefault(sectionId, General);

    /// <summary>One sample touching every page, for "preview my style on everything".</summary>
    public const string General = """
        DECLARE @CustomerId int = 42, @From date = '2026-01-01';
        WITH RecentOrders (OrderId, CustomerId, Total) AS (SELECT o.OrderId, o.CustomerId, SUM(d.UnitPrice * d.Quantity) AS Total FROM dbo.Orders AS o INNER JOIN dbo.OrderDetails AS d ON d.OrderId = o.OrderId WHERE o.OrderDate >= @From GROUP BY o.OrderId, o.CustomerId)
        SELECT DISTINCT TOP (10) c.CustomerId, c.CompanyName AS Company, -- the company
         r.Total, CASE WHEN r.Total > 1000 THEN 'Large' WHEN r.Total > 100 THEN 'Medium' ELSE 'Small' END AS Size, COALESCE(c.Region, c.Country, 'n/a') AS Area
        FROM dbo.Customers AS c LEFT JOIN RecentOrders AS r ON r.CustomerId = c.CustomerId AND r.Total > 0
        WHERE c.CustomerId = @CustomerId AND (c.Country IN ('Egypt', 'France', 'Germany') OR c.Region IS NULL) AND r.Total BETWEEN 10 AND 5000
        ORDER BY r.Total DESC, c.CompanyName;
        INSERT INTO dbo.AuditLog (EventTime, UserName, Action) VALUES (SYSDATETIME(), SUSER_SNAME(), 'Report');
        IF @@ROWCOUNT = 0 BEGIN PRINT 'nothing'; RETURN; END;
        """;

    private static readonly Dictionary<string, string> Samples = new(StringComparer.OrdinalIgnoreCase)
    {
        ["whitespace"] = """
            IF @Report = 1
            BEGIN
                /* Customers report
                         for the regional team */
                SELECT c.CustomerId, CONCAT(c.FirstName, ' ', c.LastName, ' <', c.EmailAddress, '> from ', c.City, ', ', c.Region, ', ', c.Country) AS Label FROM dbo.Customers AS c WHERE c.Country = 'Egypt';
            END;


            UPDATE dbo.Customers SET Reviewed = 1 WHERE CustomerId = 42;
            GO


            SELECT COUNT(*) FROM dbo.Orders;
            """,
        ["lists"] = """
            SELECT c.CustomerId, c.CompanyName AS Company, -- trading name
             c.ContactName AS Contact, COALESCE(c.Phone, c.Mobile) AS PhoneNumber -- main line
            FROM dbo.Customers AS c, dbo.Regions AS r
            WHERE c.RegionId = r.RegionId
            GROUP BY c.CustomerId, c.CompanyName, c.ContactName, c.Phone, c.Mobile
            ORDER BY c.CompanyName, c.CustomerId;
            """,
        ["parentheses"] = """
            SELECT o.OrderId, (o.Freight + o.Tax) * 1.1 AS Charged
            FROM dbo.Orders AS o
            WHERE (o.ShipCountry = 'Egypt' OR o.ShipCountry = 'France' OR o.ShipRegion IS NULL)
              AND o.CustomerId IN (SELECT c.CustomerId FROM dbo.Customers AS c WHERE c.Active = 1 AND c.CreditLimit > 1000)
              AND EXISTS (SELECT 1 FROM dbo.OrderDetails AS d WHERE d.OrderId = o.OrderId);
            """,
        ["casing"] = """
            Select TOP (5) GetDate() as Now, cast(o.Total AS decimal(10, 2)) as Total, CAST(o.Qty as INT) as Qty, Upper(o.Status) as Status, @@RowCount as Rows
            From dbo.Orders as o
            where o.OrderDate >= DateAdd(day, -7, SysDateTime());
            """,
        ["dml"] = """
            SELECT DISTINCT TOP (10) c.CustomerId, c.CompanyName, SUM(o.Total) AS Spent FROM dbo.Customers AS c, dbo.Orders AS o WHERE o.CustomerId = c.CustomerId AND o.Status = 'Shipped' AND o.Total > 0 GROUP BY c.CustomerId, c.CompanyName HAVING SUM(o.Total) > 1000 ORDER BY Spent DESC, c.CompanyName;
            INSERT INTO dbo.TopCustomers (CustomerId) SELECT c.CustomerId FROM dbo.Customers AS c WHERE c.CustomerId IN (SELECT o.CustomerId FROM dbo.Orders AS o);
            UPDATE dbo.Customers SET Reviewed = 1 WHERE CustomerId = 42;
            DELETE FROM dbo.Sessions WHERE Expired = 1;
            """,
        ["ddl"] = """
            CREATE TABLE dbo.Widgets (WidgetId int NOT NULL IDENTITY(1, 1) CONSTRAINT PK_Widgets PRIMARY KEY CLUSTERED, Name nvarchar(200) NOT NULL, Price decimal(10, 2) NULL CONSTRAINT DF_Widgets_Price DEFAULT (0), CategoryId int NOT NULL, CONSTRAINT FK_Widgets_Category FOREIGN KEY (CategoryId, Name) REFERENCES dbo.Categories (CategoryId, Name)) ON [PRIMARY];
            CREATE TABLE dbo.Tags (TagId int NOT NULL, Name nvarchar(50) NULL);
            GO
            CREATE PROCEDURE dbo.GetWidgets @CategoryId int, @MinPrice decimal(10, 2) = 0, @Top int = 50 AS SELECT TOP (@Top) w.WidgetId, w.Name FROM dbo.Widgets AS w WHERE w.CategoryId = @CategoryId AND w.Price >= @MinPrice;
            GO
            CREATE PROCEDURE dbo.GetTag @TagId int AS SELECT t.Name FROM dbo.Tags AS t WHERE t.TagId = @TagId;
            """,
        ["controlFlow"] = """
            IF @Count = 0 BEGIN PRINT 'Nothing to do'; RETURN; END ELSE BEGIN UPDATE dbo.Jobs SET Status = 'Queued' WHERE JobId = @JobId; END;
            WHILE @Remaining > 0 BEGIN SET @Remaining = @Remaining - 1; END;
            IF @Debug = 1 PRINT 'debug';
            """,
        ["cte"] = """
            WITH RecentOrders (OrderId, CustomerId, Total) AS (SELECT o.OrderId, o.CustomerId, o.Total FROM dbo.Orders AS o WHERE o.OrderDate >= '2026-01-01'), BigSpenders AS (SELECT r.CustomerId, SUM(r.Total) AS Spent FROM RecentOrders AS r GROUP BY r.CustomerId)
            SELECT b.CustomerId, b.Spent FROM BigSpenders AS b WHERE b.Spent > 1000;
            """,
        ["variables"] = """
            DECLARE @CustomerId int = 42, @From date = '2026-01-01', @Name nvarchar(100), @Total decimal(10, 2) = 0;
            SET @Name = N'Test';
            SET @Name = N'A long message that goes on and on and on' + N' until the line is much longer than the wrap width allows it to be';
            """,
        ["joinStatements"] = """
            SELECT c.CompanyName, o.OrderId, d.ProductId
            FROM dbo.Customers AS c INNER JOIN dbo.Orders AS o ON o.CustomerId = c.CustomerId AND o.Status = 'Shipped' LEFT OUTER JOIN dbo.OrderDetails AS d ON d.OrderId = o.OrderId CROSS APPLY dbo.fn_Discounts(o.OrderId) AS x
            WHERE c.Country = 'Egypt';
            """,
        ["insertStatements"] = """
            INSERT INTO dbo.AuditLog (EventTime, UserName, Action, Details) VALUES (SYSDATETIME(), SUSER_SNAME(), 'Report', 'Customers by size and region for the regional sales team, sent weekly on Mondays'), (SYSDATETIME(), SUSER_SNAME(), 'Export', 'Orders for the last quarter, all regions, including cancelled orders and returns');
            """,
        ["functionCalls"] = """
            SELECT GETDATE() AS Now, COALESCE(c.Region, c.Country, 'n/a') AS Area, CONCAT(c.FirstName, ' ', c.MiddleName, ' ', c.LastName, ' <', c.EmailAddress, '> from ', c.City, ', ', c.Country) AS Label, ROUND(SUM(o.Total), 2) AS Spent
            FROM dbo.Customers AS c JOIN dbo.Orders AS o ON o.CustomerId = c.CustomerId
            GROUP BY c.Region, c.Country, c.FirstName, c.MiddleName, c.LastName, c.EmailAddress, c.City;
            """,
        ["caseExpressions"] = """
            SELECT o.OrderId, CASE WHEN o.Total > 1000 THEN 'Large' WHEN o.Total > 100 THEN 'Medium' ELSE 'Small' END AS Size, CASE o.Status WHEN 'S' THEN 'Shipped' WHEN 'P' THEN 'Pending' ELSE 'Unknown' END AS StatusName, CASE WHEN o.Paid = 1 THEN 'Yes' ELSE 'No' END AS Paid
            FROM dbo.Orders AS o;
            """,
        ["operators"] = """
            SELECT o.OrderId, o.Price*o.Quantity-o.Discount AS Net
            FROM dbo.Orders AS o
            WHERE o.Status='Shipped' AND o.CustomerCode<>'X' OR o.Total>=1000 AND o.OrderDate BETWEEN '2026-01-01' AND '2026-12-31' AND o.ShipCountry IN ('Egypt', 'France', 'Germany', 'United Kingdom', 'Spain', 'Italy', 'Portugal', 'Netherlands', 'Belgium');
            UPDATE dbo.Orders SET Flag = 1 WHERE Paid = 1 AND Status IN ('S', 'P');
            """,
    };
}
