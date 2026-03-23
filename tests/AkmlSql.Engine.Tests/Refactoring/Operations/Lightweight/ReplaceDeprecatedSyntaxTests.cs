using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// T035 — Unit tests for ReplaceDeprecatedSyntaxOperation.
/// </summary>
public sealed class ReplaceDeprecatedSyntaxTests
{
    private readonly ReplaceDeprecatedSyntaxOperation _op = new();

    [Fact]
    public void ReplaceDeprecatedSyntax_NotEqualsOperator_ReplacedWithStandard()
    {
        const string sql = "SELECT * FROM dbo.Orders WHERE x != 5";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Contains("<>", result);
        Assert.DoesNotContain("!=", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ReplaceDeprecatedSyntax_NoDeprecatedSyntax_ReturnsUnchanged()
    {
        const string sql = "SELECT Id, Name FROM dbo.Customers WHERE Id = 1";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ReplaceDeprecatedSyntax_MultipleNotEquals_AllReplaced()
    {
        const string sql = "SELECT * FROM t WHERE a != b AND c != d";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.DoesNotContain("!=", result);
        var occurrences = CountOccurrences(result, "<>");
        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void ReplaceDeprecatedSyntax_EmptyInput_ReturnsEmpty()
    {
        var ctx = LightweightOperationTestHelper.CreateContext(string.Empty);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(string.Empty, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ReplaceDeprecatedSyntax_OldOuterJoinOperator_EmitsWarning()
    {
        // The *= operator is an old-style outer join — should warn, not auto-replace
        const string sql = "SELECT * FROM t1, t2 WHERE t1.Id *= t2.Id";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("Manual conversion required"));
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int idx   = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx++;
        }
        return count;
    }
}
