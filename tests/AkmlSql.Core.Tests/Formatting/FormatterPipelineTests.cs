using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Core.Tests.Formatting;

/// <summary>
/// Tests for <see cref="FormatterPipeline"/>. These run the full 7-stage pipeline
/// end-to-end using a default profile.
/// </summary>
public class FormatterPipelineTests
{
    private readonly FormatterPipeline _pipeline = new();
    private static FormattingProfile DefaultProfile => new();

    // ── Basic formatting ──────────────────────────────────────────────────────

    [Fact]
    public void Format_ReturnsSuccess_ForValidSql()
    {
        var result = _pipeline.Format("select 1", DefaultProfile);
        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.FormattedText));
    }

    [Fact]
    public void Format_UppercasesKeywords_ByDefault()
    {
        var result = _pipeline.Format("select 1 from dbo.orders", DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("SELECT", result.FormattedText);
        Assert.Contains("FROM", result.FormattedText);
    }

    [Fact]
    public void Format_ReportsElapsedTime()
    {
        var result = _pipeline.Format("SELECT 1", DefaultProfile);
        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public void Format_SetsWasModified_WhenTextChanges()
    {
        // lowercase input → uppercase output = modified
        var result = _pipeline.Format("select 1", DefaultProfile);
        Assert.True(result.WasModified);
    }

    [Fact]
    public void Format_SetsWasModified_False_WhenAlreadyFormatted()
    {
        // Format once to get canonical form, then format again
        var first = _pipeline.Format("SELECT 1", DefaultProfile);
        var second = _pipeline.Format(first.FormattedText, DefaultProfile);
        Assert.False(second.WasModified);
    }

    // ── Semantic validation ───────────────────────────────────────────────────

    [Fact]
    public void Format_SemanticValidationPasses_ForValidSql()
    {
        var result = _pipeline.Format("SELECT Id, Name FROM dbo.Orders WHERE Id > 0", DefaultProfile);
        Assert.True(result.ValidationPassed);
    }

    // ── Empty / whitespace input ──────────────────────────────────────────────

    [Fact]
    public void Format_HandlesEmptyInput()
    {
        // Should not throw — returns empty or original
        var result = _pipeline.Format("", DefaultProfile);
        Assert.NotNull(result);
    }

    [Fact]
    public void Format_HandlesWhitespaceOnlyInput()
    {
        var result = _pipeline.Format("   \n\t  ", DefaultProfile);
        Assert.NotNull(result);
    }

    // ── noformat regions ──────────────────────────────────────────────────────

    [Fact]
    public void Format_PreservesNoformatRegion()
    {
        var sql = """
            SELECT 1

            -- noformat
            SELECT   weird   spacing
            -- endnoformat

            SELECT 2
            """;
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        // The weird spacing should be preserved inside the noformat region
        Assert.Contains("weird   spacing", result.FormattedText);
    }

    // ── Multi-statement SQL ───────────────────────────────────────────────────

    [Fact]
    public void Format_HandlesMultipleBatches()
    {
        var sql = "SELECT 1\nGO\nSELECT 2\nGO";
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("SELECT", result.FormattedText);
    }

    // ── Complex SQL ───────────────────────────────────────────────────────────

    [Fact]
    public void Format_HandlesJoinQuery()
    {
        var sql = """
            select o.Id, c.Name from dbo.Orders o
            inner join dbo.Customers c on o.CustomerId = c.Id
            where o.Status = 'Active'
            order by o.Id
            """;
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("SELECT", result.FormattedText);
        Assert.Contains("JOIN", result.FormattedText);
    }

    [Fact]
    public void Format_HandlesCte()
    {
        var sql = """
            with cte as (select Id from dbo.Orders)
            select * from cte
            """;
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("WITH", result.FormattedText);
    }

    [Fact]
    public void Format_HandlesDdl()
    {
        var sql = """
            create table dbo.Test (Id int primary key, Name varchar(100) not null)
            """;
        var result = _pipeline.Format(sql, DefaultProfile);
        Assert.True(result.Success);
        Assert.Contains("CREATE TABLE", result.FormattedText);
    }

    // ── Profile: skip validation ──────────────────────────────────────────────

    [Fact]
    public void Format_WithSkipValidation_StillFormats()
    {
        var profile = new FormattingProfile();
        profile.Metadata.SkipValidation = true;
        var result = _pipeline.Format("select 1 from dbo.t", profile);
        Assert.True(result.Success);
        Assert.Contains("SELECT", result.FormattedText);
    }

    // ── Profile: idempotency check disabled ───────────────────────────────────

    [Fact]
    public void Format_WithIdempotencyCheckDisabled_StillSucceeds()
    {
        var profile = new FormattingProfile();
        profile.Metadata.EnableIdempotencyCheck = false;
        var result = _pipeline.Format("select 1", profile);
        Assert.True(result.Success);
        Assert.Contains("SELECT", result.FormattedText);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public void Format_IsIdempotent_ForTypicalQueries()
    {
        var sqls = new[]
        {
            "SELECT Id, Name FROM dbo.Orders WHERE Id > 0 ORDER BY Id",
            "INSERT INTO dbo.Orders (Status) VALUES ('Active')",
            "UPDATE dbo.Orders SET Status = 'Closed' WHERE Id = 1",
            "DELETE FROM dbo.Orders WHERE Id = 1",
        };

        foreach (var sql in sqls)
        {
            var first = _pipeline.Format(sql, DefaultProfile);
            var second = _pipeline.Format(first.FormattedText, DefaultProfile);
            Assert.Equal(first.FormattedText, second.FormattedText);
        }
    }
}
