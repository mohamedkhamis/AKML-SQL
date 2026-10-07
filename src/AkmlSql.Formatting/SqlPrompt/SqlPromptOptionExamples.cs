namespace AkmlSql.Formatting.SqlPrompt;

/// <summary>A sample query the style editors format to show what an option does.</summary>
public sealed record SqlPromptExampleQuery(string Id, string Title, string Sql);

/// <summary>
/// Where one option shows: the query to format, and the other options that must be set first for
/// it to show (an IN list wraps only when its values go on new lines, say). The option's own
/// enabling switch is not listed here: <see cref="SqlPromptOptionExamples.SettingsFor"/> adds it.
/// </summary>
public sealed record SqlPromptOptionExample(SqlPromptExampleQuery Query, IReadOnlyList<KeyValuePair<string, string>> With);

/// <summary>
/// The "what does each value do" examples behind the style editors: for every SQL Prompt option, a
/// short Northwind query (a SELECT wherever the option acts on one) on which every value of the
/// option formats differently under SQL Prompt's defaults. <c>SqlPromptOptionExampleTests</c>
/// pins that promise, value by value, so a layout change that hides an example fails a test
/// instead of quietly showing identical cards.
/// <para>
/// A few values only show on another query (an "always" IN-list break needs a list that would not
/// wrap anyway) — <see cref="For(string, string)"/> returns that query for them. Options that never
/// act on a SELECT (DDL, INSERT, control flow) use their page's own preview sample.
/// </para>
/// </summary>
public static class SqlPromptOptionExamples
{
    /// <summary>Query id that stands for "the option's page sample" (<see cref="SqlPromptPreviewSamples"/>).</summary>
    public const string PageSampleId = "page";

    /// <summary>The example queries, Northwind tables throughout.</summary>
    public static IReadOnlyList<SqlPromptExampleQuery> Queries { get; } =
    [
        new("basic", "Customers in Germany", """
            SELECT c.CustomerID, c.CompanyName AS Company, c.ContactName AS Contact, c.Country FROM dbo.Customers AS c WHERE c.Country = 'Germany' AND c.City <> 'Berlin' ORDER BY c.CompanyName, c.CustomerID;
            """),
        new("short", "One short query", """
            SELECT c.CompanyName FROM dbo.Customers AS c WHERE c.Country = 'UK';
            """),
        new("join", "Revenue per customer (JOIN, GROUP BY, HAVING)", """
            SELECT c.CompanyName, COUNT(o.OrderID) AS Orders, SUM(od.UnitPrice * od.Quantity) AS Revenue FROM dbo.Customers AS c INNER JOIN dbo.Orders AS o ON o.CustomerID = c.CustomerID AND o.ShippedDate IS NOT NULL LEFT OUTER JOIN dbo.[Order Details] AS od ON od.OrderID = o.OrderID WHERE o.OrderDate >= '1997-01-01' GROUP BY c.CompanyName HAVING COUNT(o.OrderID) > 5 ORDER BY Revenue DESC;
            """),
        new("topcomments", "Top products (DISTINCT TOP, aliases, comments)", """
            SELECT DISTINCT TOP (10) p.ProductID, p.ProductName AS Product, -- display name
             p.UnitPrice AS Price, p.UnitsInStock AS Stock -- on hand
            FROM dbo.Products AS p WHERE p.Discontinued = 0 ORDER BY p.UnitPrice DESC;
            """),
        new("case", "Price bands (CASE)", """
            SELECT p.ProductName, CASE WHEN p.UnitPrice > 50 THEN 'Premium' WHEN p.UnitPrice > 20 THEN 'Standard' ELSE 'Budget' END AS PriceBand, CASE p.CategoryID WHEN 1 THEN 'Beverages' WHEN 2 THEN 'Condiments' ELSE 'Other' END AS Category, CASE WHEN p.Discontinued = 1 THEN 'Yes' ELSE 'No' END AS Retired FROM dbo.Products AS p;
            """),
        new("subquery", "Orders shipped to France or Spain (parentheses, IN, EXISTS)", """
            SELECT o.OrderID, (o.Freight + 10) * 1.14 AS ShippingCost FROM dbo.Orders AS o WHERE (o.ShipCountry = 'France' OR o.ShipCountry = 'Spain' OR o.ShipRegion IS NULL) AND o.CustomerID IN (SELECT c.CustomerID FROM dbo.Customers AS c WHERE c.Country = 'France' AND c.City <> 'Paris') AND EXISTS (SELECT 1 FROM dbo.[Order Details] AS od WHERE od.OrderID = o.OrderID);
            """),
        new("operators", "Order lines (operators, BETWEEN, long IN list)", """
            SELECT od.OrderID, od.UnitPrice*od.Quantity*(1-od.Discount) AS LineTotal FROM dbo.[Order Details] AS od JOIN dbo.Orders AS o ON o.OrderID=od.OrderID WHERE od.Quantity>=10 AND od.Discount<>0 OR o.Freight>100 AND o.OrderDate BETWEEN '1997-01-01' AND '1997-12-31' AND o.ShipCountry IN ('Germany', 'France', 'Brazil', 'UK', 'USA', 'Spain', 'Italy', 'Portugal', 'Venezuela', 'Argentina', 'Mexico', 'Austria');
            """),
        new("functions", "Freight by employee (function calls)", """
            SELECT GETDATE() AS Today, COALESCE(c.Region, c.Country, 'n/a') AS Area, CONCAT(e.FirstName, ' ', e.LastName, ' (', e.Title, ') handles orders shipped to ', o.ShipCity, ', ', o.ShipCountry, ' ', o.ShipPostalCode) AS Label, ROUND(SUM(o.Freight), 2) AS Freight FROM dbo.Orders AS o JOIN dbo.Employees AS e ON e.EmployeeID = o.EmployeeID JOIN dbo.Customers AS c ON c.CustomerID = o.CustomerID GROUP BY c.Region, c.Country, e.FirstName, e.LastName, e.Title, o.ShipCity, o.ShipCountry, o.ShipPostalCode;
            """),
        new("window", "Latest order per customer (window function)", """
            SELECT o.CustomerID, o.OrderID, ROW_NUMBER() OVER (PARTITION BY o.CustomerID ORDER BY o.OrderDate DESC) AS RowNo, SUM(o.Freight) OVER (PARTITION BY o.CustomerID) AS CustomerFreight FROM dbo.Orders AS o WHERE o.OrderDate >= DATEADD(YEAR, -1, GETDATE());
            """),
        new("cte", "Big spenders (CTE)", """
            WITH OrderTotals (OrderID, CustomerID, Total) AS (SELECT o.OrderID, o.CustomerID, SUM(od.UnitPrice * od.Quantity) FROM dbo.Orders AS o JOIN dbo.[Order Details] AS od ON od.OrderID = o.OrderID GROUP BY o.OrderID, o.CustomerID), BigSpenders AS (SELECT t.CustomerID, SUM(t.Total) AS Spent FROM OrderTotals AS t GROUP BY t.CustomerID) SELECT b.CustomerID, b.Spent FROM BigSpenders AS b WHERE b.Spent > 10000;
            """),
        new("casing", "Mixed-case query (casing)", """
            select top (5) GetDate() as Today, cast(p.UnitPrice as Decimal(10, 2)) as Price, Upper(p.ProductName) as Name, @@RowCount as Rows from dbo.Products as p where p.UnitPrice > 10;
            """),
        new("batches", "Several SELECTs, blank lines and GO", """
            SELECT COUNT(*) AS Customers FROM dbo.Customers;
            
            
            
            SELECT COUNT(*) AS Orders FROM dbo.Orders;
            GO
            
            
            
            SELECT COUNT(*) AS Products FROM dbo.Products;
            """),
        new("comment", "SELECT under IF with a multi-line comment", """
            IF @Report = 1
            BEGIN
                        /* Products report
                           for the purchasing team */
                SELECT p.ProductName, p.UnitPrice FROM dbo.Products AS p WHERE p.Discontinued = 0;
            END
            """),
        new("wide", "A long expression that needs wrapping", """
            SELECT c.CustomerID, CONCAT(c.ContactName, ' <', c.ContactTitle, '> at ', c.CompanyName, ', ', c.Address, ', ', c.City, ' ', c.PostalCode, ', ', c.Country) AS MailingLabel FROM dbo.Customers AS c WHERE c.Country = 'Mexico';
            """),
        new("shortin", "Products in three categories (short IN list)", """
            SELECT p.ProductName, p.UnitPrice FROM dbo.Products AS p WHERE p.CategoryID IN (1, 2, 8) AND p.Discontinued = 0;
            """),
        new("selectvar", "SELECT into variables", """
            DECLARE @Customers int, @LastOrder datetime, @CompanyName nvarchar(40);
            SELECT @Customers = COUNT(*), @LastOrder = MAX(o.OrderDate) FROM dbo.Orders AS o;
            SET @CompanyName = N'Alfreds Futterkiste' + N' — a long company description that is wider than the wrap width allows';
            """),
    ];

    // Static initializers run in declaration order: this must follow Queries and precede SelectExamples.
    private static readonly Dictionary<string, SqlPromptExampleQuery> QueriesById =
        Queries.ToDictionary(q => q.Id, StringComparer.Ordinal);

    /// <summary>
    /// The SELECT statements the editors' preview offers next to the page sample ("SELECT
    /// examples"), from simple to involved.
    /// </summary>
    public static IReadOnlyList<SqlPromptExampleQuery> SelectExamples { get; } =
        new[] { "basic", "join", "topcomments", "case", "subquery", "operators", "functions", "window", "cte" }
            .Select(Query).ToList();

    public static SqlPromptExampleQuery Query(string id) => QueriesById[id];

    /// <summary>The example for <paramref name="path"/>: its query and the options it needs set.</summary>
    public static SqlPromptOptionExample For(string path) => Resolve(path, Table[path]);

    /// <summary>The example for one value of <paramref name="path"/> — the option's own example
    /// unless that value only shows on another query.</summary>
    public static SqlPromptOptionExample For(string path, string value)
    {
        var entry = Table[path];
        if (entry.ByValue is { } byValue && byValue.TryGetValue(value, out var own))
            return Resolve(path, own with { With = [.. entry.With, .. own.With] });
        return Resolve(path, entry);
    }

    /// <summary>
    /// Everything to set, besides the option itself, before formatting its example: the option's
    /// enabling switch (a threshold needs its collapse on) and the example's own settings.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> SettingsFor(string path, string value)
    {
        var option = SqlPromptOptionCatalog.Find(path) ?? throw new ArgumentException($"'{path}' is not a SQL Prompt formatting option.", nameof(path));
        var settings = new List<KeyValuePair<string, string>>();
        if (option.EnabledWhen is { } gate) settings.Add(new(gate.Path, gate.Value));
        settings.AddRange(For(path, value).With);
        return settings;
    }

    /// <summary>
    /// The values the editors show a card for: on/off, every choice, or for a number its default
    /// and two values either side that change the example. Default first.
    /// </summary>
    public static IReadOnlyList<string> ValuesFor(SqlPromptOption option) => option.Kind switch
    {
        SqlPromptOptionKind.Boolean => option.Default == "true" ? ["true", "false"] : ["false", "true"],
        SqlPromptOptionKind.Integer => [option.Default, .. (IntegerValues.GetValueOrDefault(option.Path) ?? ["40", "200"]).Where(v => v != option.Default)],
        _ => [option.Default, .. option.Choices.Select(c => c.Value).Where(v => v != option.Default)],
    };

    private static readonly Dictionary<string, string[]> IntegerValues = new(StringComparer.Ordinal)
    {
        ["whitespace.numberOfSpacesInTabs"] = ["2", "8"],
        ["whitespace.wrapLinesLongerThan"] = ["60", "200"],
        ["dml.clauses.clauseIndentation"] = ["2", "4"],
        // 200 collapses nothing more than the default 80 on the control-flow sample; 20 and 40 collapse less.
        ["controlFlow.collapseStatementsShorterThan"] = ["20", "40"],
        ["whitespace.newLines.emptyLinesBetweenStatements"] = ["0", "2"],
        ["whitespace.newLines.emptyLinesAfterBatchSeparator"] = ["0", "2"],
    };

    private static SqlPromptOptionExample Resolve(string path, Entry entry)
    {
        var query = entry.QueryId == PageSampleId
            ? new SqlPromptExampleQuery(PageSampleId, SectionLabel(path) + " sample", SqlPromptPreviewSamples.For(path[..path.IndexOf('.')]))
            : Query(entry.QueryId);
        return new SqlPromptOptionExample(query, entry.With.Select(w => new KeyValuePair<string, string>(w.Path, w.Value)).ToList());
    }

    private static string SectionLabel(string path)
    {
        var section = path[..path.IndexOf('.')];
        return SqlPromptOptionCatalog.Sections.FirstOrDefault(s => s.Id == section)?.Label ?? section;
    }

    private sealed record Entry(string QueryId, params (string Path, string Value)[] With)
    {
        public Dictionary<string, Entry>? ByValue { get; init; }
    }

    // Option → example. Chosen by trying every query above with every value of the option and
    // keeping the first on which all of them differ (SqlPromptOptionExampleTests re-checks it).
    private static readonly Dictionary<string, Entry> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["whitespace.spacesOrTabs"] = new("basic"),
        ["whitespace.numberOfSpacesInTabs"] = new("case"),
        ["whitespace.wrapLongLines"] = new("wide"),
        ["whitespace.wrapLinesLongerThan"] = new("wide") { ByValue = new Dictionary<string, Entry>(StringComparer.Ordinal) { ["60"] = new("selectvar") } },
        ["whitespace.whiteSpaceBeforeSemiColon"] = new("short"),
        ["whitespace.newLines.preserveExistingEmptyLinesBetweenStatements"] = new("batches"),
        ["whitespace.newLines.emptyLinesBetweenStatements"] = new("batches", ("whitespace.newLines.preserveExistingEmptyLinesBetweenStatements", "false")),
        ["whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator"] = new("batches"),
        ["whitespace.newLines.emptyLinesAfterBatchSeparator"] = new("batches", ("whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator", "false")),
        ["whitespace.newLines.alignMultilineCommentsMatchingPatterns"] = new("comment"),
        ["lists.placeFirstItemOnNewLine"] = new("subquery"),
        ["lists.placeSubsequentItemsOnNewLines"] = new("functions"),
        ["lists.alignSubsequentItemsWithFirstItem"] = new("basic"),
        ["lists.alignItemsAcrossClauses"] = new("basic"),
        ["lists.indentListItems"] = new("basic", ("lists.placeFirstItemOnNewLine", "always")),
        ["lists.alignItemsToTabStops"] = new("basic"),
        ["lists.alignAliases"] = new("topcomments"),
        ["lists.alignComments"] = new("topcomments"),
        ["lists.placeCommasBeforeItems"] = new("basic"),
        ["lists.addSpaceBeforeComma"] = new("basic"),
        ["lists.addSpaceAfterComma"] = new("functions"),
        ["lists.commaAlignment"] = new("basic"),
        ["parentheses.parenthesisStyle"] = new("subquery"),
        ["parentheses.indentParenthesesContents"] = new("subquery"),
        ["parentheses.collapseShortParenthesisContents"] = new("subquery"),
        ["parentheses.collapseParenthesesShorterThan"] = new("subquery"),
        ["parentheses.addSpacesAroundParentheses"] = new("topcomments"),
        ["parentheses.addSpacesInsideParentheses"] = new("topcomments"),
        ["casing.reservedKeywords"] = new("basic") { ByValue = new Dictionary<string, Entry>(StringComparer.Ordinal) { ["uppercase"] = new("casing") } },
        ["casing.builtInFunctions"] = new("casing"),
        ["casing.builtInDataTypes"] = new("casing") { ByValue = new Dictionary<string, Entry>(StringComparer.Ordinal) { ["upperCamelCase"] = new("selectvar") } },
        ["casing.globalVariables"] = new("casing"),
        ["casing.useObjectDefinitionCase"] = new("page"),
        ["dml.clauses.clauseAlignment"] = new("basic"),
        ["dml.clauses.clauseIndentation"] = new("basic"),
        ["dml.listItems.placeFromTableOnNewLine"] = new("cte"),
        ["dml.listItems.placeWhereConditionOnNewLine"] = new("subquery"),
        ["dml.listItems.placeGroupByAndOrderByOnNewLine"] = new("cte"),
        ["dml.placeInsertTableOnNewLine"] = new("page"),
        ["dml.placeDistinctAndTopClausesOnNewLine"] = new("topcomments"),
        ["dml.addNewLineAfterDistinctAndTopClauses"] = new("topcomments"),
        ["dml.collapseShortStatements"] = new("short"),
        ["dml.collapseStatementsShorterThan"] = new("short") { ByValue = new Dictionary<string, Entry>(StringComparer.Ordinal) { ["200"] = new("shortin") } },
        ["dml.collapseShortSubqueries"] = new("subquery"),
        ["dml.collapseSubqueriesShorterThan"] = new("subquery"),
        ["ddl.parenthesisStyle"] = new("page"),
        ["ddl.indentParenthesesContents"] = new("page"),
        ["ddl.alignDataTypesAndConstraints"] = new("page"),
        ["ddl.placeConstraintsOnNewLines"] = new("page"),
        ["ddl.placeConstraintColumnsOnNewLines"] = new("page"),
        ["ddl.indentClauses"] = new("page"),
        ["ddl.placeFirstProcedureParameterOnNewLine"] = new("page"),
        ["ddl.collapseShortStatements"] = new("page"),
        ["ddl.collapseStatementsShorterThan"] = new("page"),
        ["controlFlow.placeBeginAndEndOnNewLine"] = new("comment"),
        ["controlFlow.indentBeginAndEndKeywords"] = new("comment"),
        ["controlFlow.indentContentsOfStatements"] = new("comment"),
        ["controlFlow.collapseShortStatements"] = new("page"),
        ["controlFlow.collapseStatementsShorterThan"] = new("page"),
        ["cte.parenthesisStyle"] = new("cte"),
        ["cte.indentContents"] = new("cte", ("whitespace.numberOfSpacesInTabs", "2")),
        ["cte.placeNameOnNewLine"] = new("cte"),
        ["cte.indentName"] = new("cte"),
        ["cte.placeColumnsOnNewLine"] = new("cte"),
        ["cte.columnAlignment"] = new("cte"),
        ["cte.placeAsOnNewLine"] = new("cte"),
        ["cte.asAlignment"] = new("cte"),
        ["variables.alignDataTypesAndValues"] = new("selectvar"),
        ["variables.addSpaceBetweenDataTypeAndPrecision"] = new("selectvar"),
        ["variables.placeAssignedValueOnNewLineIfLongerThanMaxLineLength"] = new("page"),
        ["variables.placeEqualsSignOnNewLine"] = new("page"),
        ["joinStatements.join.placeOnNewLine"] = new("join"),
        ["joinStatements.join.keywordAlignment"] = new("join") { ByValue = new Dictionary<string, Entry>(StringComparer.Ordinal) { ["rightAlignedToFrom"] = new("join", ("dml.clauses.clauseAlignment", "rightAligned")) } },
        ["joinStatements.join.insertEmptyLineBetweenJoinClauses"] = new("join"),
        ["joinStatements.join.placeJoinTableOnNewLine"] = new("join"),
        ["joinStatements.join.indentJoinTable"] = new("join"),
        ["joinStatements.on.placeOnNewLine"] = new("join"),
        ["joinStatements.on.keywordAlignment"] = new("join"),
        ["joinStatements.on.placeConditionOnNewLine"] = new("join"),
        ["joinStatements.on.conditionAlignment"] = new("join", ("joinStatements.on.keywordAlignment", "indented"), ("joinStatements.on.placeConditionOnNewLine", "true")),
        ["insertStatements.columns.parenthesisStyle"] = new("page"),
        ["insertStatements.columns.indentContents"] = new("page"),
        ["insertStatements.columns.placeSubsequentColumnsOnNewLines"] = new("page"),
        ["insertStatements.values.parenthesisStyle"] = new("page", ("insertStatements.values.placeSubsequentValuesOnNewLines", "always")),
        ["insertStatements.values.indentContents"] = new("page", ("insertStatements.values.placeSubsequentValuesOnNewLines", "always")),
        ["insertStatements.values.placeSubsequentValuesOnNewLines"] = new("page"),
        ["functionCalls.placeArgumentsOnNewLines"] = new("functions"),
        ["functionCalls.addSpacesAroundParentheses"] = new("join"),
        ["functionCalls.addSpacesAroundArgumentList"] = new("join"),
        ["functionCalls.addSpaceBetweenEmptyParentheses"] = new("functions"),
        ["caseExpressions.placeExpressionOnNewLine"] = new("case"),
        ["caseExpressions.placeFirstWhenOnNewLine"] = new("case"),
        ["caseExpressions.whenAlignment"] = new("case", ("caseExpressions.placeFirstWhenOnNewLine", "never")),
        ["caseExpressions.placeThenOnNewLine"] = new("case"),
        ["caseExpressions.thenAlignment"] = new("case"),
        ["caseExpressions.placeElseOnNewLine"] = new("case"),
        ["caseExpressions.alignElseToWhen"] = new("case"),
        ["caseExpressions.placeEndOnNewLine"] = new("case"),
        ["caseExpressions.endAlignment"] = new("case"),
        ["caseExpressions.collapseShortCaseExpressions"] = new("case"),
        ["caseExpressions.collapseCaseExpressionsShorterThan"] = new("case"),
        ["operators.comparison.align"] = new("basic"),
        ["operators.comparison.addSpacesAround"] = new("basic"),
        ["operators.arithmetic.addSpacesAround"] = new("join"),
        ["operators.andOr.placeOnNewLine"] = new("subquery"),
        ["operators.andOr.alignment"] = new("basic"),
        ["operators.andOr.placeKeywordBeforeCondition"] = new("basic"),
        ["operators.between.placeOnNewLine"] = new("operators"),
        ["operators.between.placeAndKeywordOnNewLine"] = new("operators"),
        ["operators.between.andAlignment"] = new("operators"),
        ["operators.in.placeOpeningParenthesisOnNewLine"] = new("operators"),
        ["operators.in.alignment"] = new("operators", ("operators.in.placeSubsequentValuesOnNewLines", "always")),
        ["operators.in.placeFirstValueOnNewLine"] = new("shortin") { ByValue = new Dictionary<string, Entry>(StringComparer.Ordinal) { ["never"] = new("operators") } },
        ["operators.in.placeSubsequentValuesOnNewLines"] = new("operators") { ByValue = new Dictionary<string, Entry>(StringComparer.Ordinal) { ["always"] = new("shortin") } },
        ["operators.in.addSpaceAroundInContents"] = new("operators"),
    };
}
