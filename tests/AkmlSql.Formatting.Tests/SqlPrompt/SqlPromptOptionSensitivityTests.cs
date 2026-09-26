using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using Xunit;

namespace AkmlSql.Formatting.Tests.SqlPrompt;

/// <summary>
/// "Change an option, the preview changes": for every SQL Prompt option, every value other than
/// the default must change the formatted output — in a scenario where the option applies (an IN
/// list has to be long enough to wrap before "place subsequent values on new lines" can show).
/// Choice and on/off options must react to each value; a threshold must react to at least one
/// of the values tried. The one exemption needs a live database and says so.
/// </summary>
public class SqlPromptOptionSensitivityTests
{
    internal const string Rich = """
        DECLARE @CustomerId int = 42, @From date = '2026-01-01', @Name nvarchar(100);
        SET @Name = N'Test';
        WITH RecentOrders (OrderId, CustomerId, Total) AS (SELECT o.OrderId, o.CustomerId, SUM(d.UnitPrice * d.Quantity) AS Total FROM dbo.Orders AS o INNER JOIN dbo.OrderDetails AS d ON d.OrderId = o.OrderId AND d.Quantity > 0 WHERE o.OrderDate >= @From GROUP BY o.OrderId, o.CustomerId)
        SELECT DISTINCT TOP (10) c.CustomerId, c.CompanyName AS Company, -- the company
         r.Total AS T, CASE WHEN r.Total > 1000 THEN 'Large' WHEN r.Total > 100 THEN 'Medium' ELSE 'Small' END AS Size, CASE r.Kind WHEN 1 THEN 'One' ELSE 'Other' END AS KindName, GETDATE() AS Now, COALESCE(c.Region, c.Country, 'n/a') AS Area -- area
        FROM dbo.Customers AS c LEFT OUTER JOIN RecentOrders AS r ON r.CustomerId = c.CustomerId LEFT JOIN dbo.Regions AS g ON g.RegionId = c.RegionId AND g.Active = 1
        WHERE c.CustomerId = @CustomerId AND (c.Country IN ('Egypt', 'France', 'Germany') OR c.Region IS NULL) AND r.Total BETWEEN 10 AND 5000 AND c.CustomerId NOT IN (SELECT b.CustomerId FROM dbo.Blocked AS b WHERE b.Reason <> 'x')
        GROUP BY c.CustomerId, c.CompanyName HAVING COUNT(*) > 1 ORDER BY r.Total DESC, c.CompanyName;


        SELECT a.x+b.y*2 AS calc FROM dbo.T1 a WHERE a.flag=1;
        INSERT INTO dbo.AuditLog (EventTime, UserName, Action, Details) VALUES (SYSDATETIME(), SUSER_SNAME(), 'Report', 'Customers by size'), (SYSDATETIME(), SUSER_SNAME(), 'Report2', 'Second row');
        UPDATE dbo.Customers SET LastReviewed = GETDATE(), ReviewCount = ReviewCount + 1 WHERE CustomerId = @CustomerId;
        IF @@ROWCOUNT = 0 BEGIN PRINT 'nothing updated'; RETURN; END ELSE PRINT 'done';
        IF @CustomerId = 1 PRINT 'one';
        CREATE TABLE dbo.Widgets (WidgetId int NOT NULL IDENTITY(1, 1) CONSTRAINT PK_Widgets PRIMARY KEY CLUSTERED, Name nvarchar(200) NOT NULL, Price decimal(10, 2) NULL CONSTRAINT DF_Widgets_Price DEFAULT (0), CONSTRAINT FK_Widgets_Category FOREIGN KEY (CategoryId, Name) REFERENCES dbo.Categories (CategoryId, Name)) ON [PRIMARY];
        CREATE TABLE dbo.T (A int);
        GO
        CREATE PROCEDURE dbo.P @a int, @b int = 2 AS SELECT @a;
        GO

        CREATE PROCEDURE dbo.Q @only int AS SELECT @only;
        """;

    private const string LongSet =
        "SET @Name = N'A very long string value that pushes this assignment' + N' well beyond the wrap width of one hundred and twenty';";

    private const string LongInList =
        "SELECT * FROM dbo.T WHERE t.Country IN ('Egypt', 'France', 'Germany', 'United Kingdom', 'Spain', 'Italy', 'Portugal', 'Netherlands', 'Belgium', 'Austria');";

    private const string LongValues =
        "INSERT INTO dbo.T (A, B, C) VALUES ('a fairly long first value here', 'a second quite long value here', 'and a third long value to overflow the line');";

    private const string LongCall =
        "SELECT CONCAT(c.FirstName, ' ', c.MiddleName, ' ', c.LastName, ' <', c.EmailAddress, '> from ', c.City, ', ', c.Region, ', ', c.Country, ' ', c.PostalCode) AS Label FROM dbo.C AS c;";

    private const string MixedCase =
        "sElEcT gEtDaTe() aS Now, cAsT(1 aS iNt) aS n, @@rOwCoUnT aS c fRoM dbo.T;";

    /// <summary>Where an option only shows in a particular construct or next to another setting.</summary>
    private static readonly Dictionary<string, (string Sql, (string Path, string Value)[] Overrides)> Scenarios = new()
    {
        ["whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator"] = ("SELECT 1;\nGO\n\n\n\nSELECT 2;\n", []),
        ["whitespace.newLines.preserveExistingEmptyLinesBetweenStatements"] = ("SELECT 1;\n\n\n\nSELECT 2;\n", []),
        ["whitespace.newLines.alignMultilineCommentsMatchingPatterns"] =
            ("IF 1 = 1\nBEGIN\n            /* first line\n               second line */\n    SELECT 1;\nEND\n", []),
        ["lists.indentListItems"] = (Rich, [("lists.placeFirstItemOnNewLine", "always")]),
        ["cte.indentContents"] = (Rich, [("whitespace.numberOfSpacesInTabs", "2")]),
        ["variables.placeAssignedValueOnNewLineIfLongerThanMaxLineLength"] = (LongSet, []),
        ["variables.placeEqualsSignOnNewLine"] = (LongSet, []),
        ["joinStatements.on.conditionAlignment"] = (Rich, [("joinStatements.on.placeConditionOnNewLine", "true"), ("joinStatements.on.keywordAlignment", "indented")]),
        ["insertStatements.values.parenthesisStyle"] = (Rich, [("insertStatements.values.placeSubsequentValuesOnNewLines", "always")]),
        ["insertStatements.values.indentContents"] = (Rich, [("insertStatements.values.placeSubsequentValuesOnNewLines", "always")]),
        ["insertStatements.values.placeSubsequentValuesOnNewLines"] = (LongValues, []),
        ["functionCalls.placeArgumentsOnNewLines"] = (LongCall, []),
        ["functionCalls.placeArgumentsOnNewLines=always"] = (Rich, []),
        ["caseExpressions.whenAlignment"] = (Rich, [("caseExpressions.placeFirstWhenOnNewLine", "never")]),
        ["operators.in.alignment"] = (LongInList, [("operators.in.placeSubsequentValuesOnNewLines", "always")]),
        // "if longer" (the default) already breaks a long list, so "always" shows on a short one
        ["operators.in.placeFirstValueOnNewLine"] = (Rich, []),
        ["operators.in.placeFirstValueOnNewLine=never"] = (LongInList, []),
        ["operators.in.placeSubsequentValuesOnNewLines"] = (LongInList, []),
        ["operators.in.placeSubsequentValuesOnNewLines=always"] = (Rich, []),
        ["casing.reservedKeywords"] = (MixedCase, []),
        ["casing.builtInFunctions"] = (MixedCase, []),
        ["casing.builtInDataTypes"] = (MixedCase, []),
        ["casing.globalVariables"] = (MixedCase, []),
    };

    /// <summary>Options that cannot change an offline preview, and why.</summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["casing.useObjectDefinitionCase"] = "Recases identifiers from their definitions in a connected database; there is no database behind a preview.",
    };

    private static readonly Dictionary<string, string[]> IntegerValues = new()
    {
        ["whitespace.numberOfSpacesInTabs"] = ["2", "8"],
        ["whitespace.wrapLinesLongerThan"] = ["40", "400"],
        ["dml.clauses.clauseIndentation"] = ["4"],
    };

    public static TheoryData<string> AllOptions()
    {
        var data = new TheoryData<string>();
        foreach (var o in SqlPromptOptionCatalog.Options) data.Add(o.Path);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllOptions))]
    public void Every_value_of_the_option_changes_the_preview(string path)
    {
        if (Exempt.ContainsKey(path)) return;
        var option = SqlPromptOptionCatalog.Find(path)!;
        var (sql, overrides) = Scenarios.TryGetValue(path, out var s) ? s : (Rich, []);

        var basis = SqlPromptStyleDocument.CreateDefault("sensitivity", "sensitivity");
        foreach (var (p, v) in overrides) basis.Set(p, v);
        if (option.EnabledWhen is { } gate) basis.Set(gate.Path, gate.Value);
        var baseline = Format(sql, basis);

        var values = option.Kind switch
        {
            SqlPromptOptionKind.Boolean => [option.Default == "true" ? "false" : "true"],
            SqlPromptOptionKind.Integer => IntegerValues.GetValueOrDefault(path) ?? ["10", "400"],
            _ => option.Choices.Select(c => c.Value).Where(v => v != option.Default).ToArray(),
        };

        var unchanged = new List<string>();
        foreach (var value in values)
        {
            // A value can have its own scenario ("path=value") when the default hides it elsewhere.
            var (valueSql, valueBasis, valueBaseline) = (sql, basis, baseline);
            if (Scenarios.TryGetValue(path + "=" + value, out var own))
            {
                valueSql = own.Sql;
                valueBasis = SqlPromptStyleDocument.CreateDefault("sensitivity", "sensitivity");
                foreach (var (p, v) in own.Overrides) valueBasis.Set(p, v);
                if (option.EnabledWhen is { } g) valueBasis.Set(g.Path, g.Value);
                valueBaseline = Format(valueSql, valueBasis);
            }
            var doc = valueBasis.Clone();
            doc.Set(path, value);
            if (Format(valueSql, doc) == valueBaseline) unchanged.Add(value);
        }

        if (option.Kind == SqlPromptOptionKind.Integer)
            Assert.True(unchanged.Count < values.Length, $"{path}: none of [{string.Join(", ", values)}] changed the preview.");
        else
            Assert.True(unchanged.Count == 0, $"{path}: [{string.Join(", ", unchanged)}] did not change the preview.");
    }

    [Fact]
    public void Exemptions_are_real_options()
    {
        Assert.All(Exempt.Keys, p => Assert.NotNull(SqlPromptOptionCatalog.Find(p)));
        Assert.All(Scenarios.Keys, p => Assert.NotNull(SqlPromptOptionCatalog.Find(p.Split('=')[0])));
    }

    private static readonly FormatterPipeline Pipeline = new();

    internal static string Format(string sql, SqlPromptStyleDocument document)
    {
        var profile = new FormattingProfile { SqlPrompt = document.Root };
        profile.Metadata.EnableIdempotencyCheck = false;
        var result = Pipeline.Format(sql, profile);
        Assert.True(result.Success, string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        Assert.True(result.ValidationPassed, "The layout changed the meaning of the SQL: " + string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        return result.FormattedText;
    }
}
