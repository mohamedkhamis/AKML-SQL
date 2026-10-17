namespace AkmlSql.Formatting.SqlPrompt;

/// <summary>
/// The preview SQL for each page of the style editor — like SQL Prompt's editor, each page
/// previews code its options act on, so changing an option visibly changes the preview.
/// </summary>
public static class SqlPromptPreviewSamples
{
    public static string For(string sectionId) => Samples.GetValueOrDefault(sectionId, General);

    /// <summary>
    /// Spec 042 (FR-145): the same page's sample written in PostgreSQL, for the Format Styles page while a PostgreSQL connection is active.
    /// The pages whose options have no meaning for PostgreSQL (control flow, variables) preview the general sample.
    /// </summary>
    public static string ForPostgreSql(string sectionId) => PostgreSqlSamples.GetValueOrDefault(sectionId, PostgreSqlGeneral);

    /// <summary>One PostgreSQL sample touching every page that applies, for "preview my style on everything".</summary>
    public const string PostgreSqlGeneral = """
        WITH recent_orders (order_id, customer_id, total) AS (SELECT o.order_id, o.customer_id, SUM(d.unit_price * d.quantity) AS total FROM sales.orders AS o INNER JOIN sales.order_details AS d ON d.order_id = o.order_id WHERE o.order_date >= '2026-01-01'::date GROUP BY o.order_id, o.customer_id)
        SELECT DISTINCT c.customer_id, c.company_name AS company, -- the company
         r.total, CASE WHEN r.total > 1000 THEN 'Large' WHEN r.total > 100 THEN 'Medium' ELSE 'Small' END AS size, COALESCE(c.region, c.country, 'n/a') AS area
        FROM sales.customers AS c LEFT JOIN recent_orders AS r ON r.customer_id = c.customer_id AND r.total > 0
        WHERE c.customer_id = 42 AND (c.country IN ('Egypt', 'France', 'Germany') OR c.region IS NULL) AND r.total BETWEEN 10 AND 5000
        ORDER BY r.total DESC NULLS LAST, c.company_name
        LIMIT 10;
        INSERT INTO sales.audit_log (event_time, user_name, action) VALUES (now(), current_user, 'Report') ON CONFLICT (event_time) DO NOTHING RETURNING id;
        """;

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

    private static readonly Dictionary<string, string> PostgreSqlSamples = new(StringComparer.OrdinalIgnoreCase)
    {
        ["whitespace"] = """
            /* Customers report
                     for the regional team */
            SELECT c.customer_id, concat(c.first_name, ' ', c.last_name, ' <', c.email_address, '> from ', c.city, ', ', c.region, ', ', c.country) AS label FROM sales.customers AS c WHERE c.country = 'Egypt';


            UPDATE sales.customers SET reviewed = true WHERE customer_id = 42;


            SELECT count(*) FROM sales.orders;
            """,
        ["lists"] = """
            SELECT c.customer_id, c.company_name AS company, -- trading name
             c.contact_name AS contact, coalesce(c.phone, c.mobile) AS phone_number -- main line
            FROM sales.customers AS c, sales.regions AS r
            WHERE c.region_id = r.region_id
            GROUP BY c.customer_id, c.company_name, c.contact_name, c.phone, c.mobile
            ORDER BY c.company_name, c.customer_id;
            """,
        ["parentheses"] = """
            SELECT o.order_id, (o.freight + o.tax) * 1.1 AS charged
            FROM sales.orders AS o
            WHERE (o.ship_country = 'Egypt' OR o.ship_country = 'France' OR o.ship_region IS NULL)
              AND o.customer_id IN (SELECT c.customer_id FROM sales.customers AS c WHERE c.active AND c.credit_limit > 1000)
              AND EXISTS (SELECT 1 FROM sales.order_details AS d WHERE d.order_id = o.order_id);
            """,
        ["casing"] = """
            Select now() as now, cast(o.total AS numeric(10, 2)) as total, o.qty::INT as qty, Upper(o.status) as status
            From sales.orders as o
            where o.order_date >= current_date - interval '7 days';
            """,
        ["dml"] = """
            SELECT DISTINCT c.customer_id, c.company_name, SUM(o.total) AS spent FROM sales.customers AS c, sales.orders AS o WHERE o.customer_id = c.customer_id AND o.status = 'Shipped' AND o.total > 0 GROUP BY c.customer_id, c.company_name HAVING SUM(o.total) > 1000 ORDER BY spent DESC, c.company_name LIMIT 10;
            INSERT INTO sales.top_customers (customer_id) SELECT c.customer_id FROM sales.customers AS c WHERE c.customer_id IN (SELECT o.customer_id FROM sales.orders AS o);
            UPDATE sales.customers SET reviewed = true WHERE customer_id = 42 RETURNING customer_id;
            DELETE FROM sales.sessions WHERE expired RETURNING session_id;
            """,
        ["ddl"] = """
            CREATE TABLE sales.widgets (widget_id integer GENERATED ALWAYS AS IDENTITY CONSTRAINT pk_widgets PRIMARY KEY, name text NOT NULL, price numeric(10, 2) NULL CONSTRAINT df_widgets_price DEFAULT 0, category_id integer NOT NULL, CONSTRAINT fk_widgets_category FOREIGN KEY (category_id, name) REFERENCES sales.categories (category_id, name));
            CREATE TABLE sales.tags (tag_id integer NOT NULL, name varchar(50) NULL);
            CREATE UNIQUE INDEX CONCURRENTLY idx_widgets_name ON sales.widgets USING btree (lower(name)) INCLUDE (price) WHERE price IS NOT NULL;
            """,
        ["cte"] = """
            WITH recent_orders (order_id, customer_id, total) AS (SELECT o.order_id, o.customer_id, o.total FROM sales.orders AS o WHERE o.order_date >= '2026-01-01'), big_spenders AS (SELECT r.customer_id, SUM(r.total) AS spent FROM recent_orders AS r GROUP BY r.customer_id)
            SELECT b.customer_id, b.spent FROM big_spenders AS b WHERE b.spent > 1000;
            """,
        ["joinStatements"] = """
            SELECT c.company_name, o.order_id, d.product_id
            FROM sales.customers AS c INNER JOIN sales.orders AS o ON o.customer_id = c.customer_id AND o.status = 'Shipped' LEFT OUTER JOIN sales.order_details AS d ON d.order_id = o.order_id CROSS JOIN LATERAL sales.discounts(o.order_id) AS x
            WHERE c.country = 'Egypt';
            """,
        ["insertStatements"] = """
            INSERT INTO sales.audit_log (event_time, user_name, action, details) VALUES (now(), current_user, 'Report', 'Customers by size and region for the regional sales team, sent weekly on Mondays'), (now(), current_user, 'Export', 'Orders for the last quarter, all regions, including cancelled orders and returns');
            """,
        ["functionCalls"] = """
            SELECT now() AS now, COALESCE(c.region, c.country, 'n/a') AS area, concat(c.first_name, ' ', c.middle_name, ' ', c.last_name, ' <', c.email_address, '> from ', c.city, ', ', c.country) AS label, round(SUM(o.total), 2) AS spent
            FROM sales.customers AS c JOIN sales.orders AS o ON o.customer_id = c.customer_id
            GROUP BY c.region, c.country, c.first_name, c.middle_name, c.last_name, c.email_address, c.city;
            """,
        ["caseExpressions"] = """
            SELECT o.order_id, CASE WHEN o.total > 1000 THEN 'Large' WHEN o.total > 100 THEN 'Medium' ELSE 'Small' END AS size, CASE o.status WHEN 'S' THEN 'Shipped' WHEN 'P' THEN 'Pending' ELSE 'Unknown' END AS status_name, CASE WHEN o.paid THEN 'Yes' ELSE 'No' END AS paid
            FROM sales.orders AS o;
            """,
        ["operators"] = """
            SELECT o.order_id, o.price*o.quantity-o.discount AS net
            FROM sales.orders AS o
            WHERE o.status='Shipped' AND o.customer_code<>'X' OR o.total>=1000 AND o.order_date BETWEEN '2026-01-01' AND '2026-12-31' AND o.ship_country IN ('Egypt', 'France', 'Germany', 'United Kingdom', 'Spain', 'Italy', 'Portugal', 'Netherlands', 'Belgium');
            UPDATE sales.orders SET flag = true WHERE paid AND status IN ('S', 'P');
            """,
    };

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
