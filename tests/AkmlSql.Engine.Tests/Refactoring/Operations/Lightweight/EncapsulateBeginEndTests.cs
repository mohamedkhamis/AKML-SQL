using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// T035 — Unit tests for EncapsulateBeginEndOperation.
/// </summary>
public sealed class EncapsulateBeginEndTests
{
    private readonly EncapsulateBeginEndOperation _op = new();

    [Fact]
    public void EncapsulateBeginEnd_NoSelection_WrapsDocument()
    {
        const string sql = "SELECT 1";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Contains("BEGIN", result);
        Assert.Contains("END", result);
        Assert.Contains("SELECT 1", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void EncapsulateBeginEnd_EmptyInput_ReturnsEmpty()
    {
        var ctx = LightweightOperationTestHelper.CreateContext(string.Empty);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(string.Empty, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void EncapsulateBeginEnd_NoSelection_BeginComesBeforeEnd()
    {
        const string sql = "SELECT col FROM dbo.Orders WHERE Id = 1";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        var beginIndex = result.IndexOf("BEGIN", StringComparison.OrdinalIgnoreCase);
        var endIndex   = result.LastIndexOf("END",  StringComparison.OrdinalIgnoreCase);

        Assert.True(beginIndex >= 0, "BEGIN not found in output");
        Assert.True(endIndex   >= 0, "END not found in output");
        Assert.True(beginIndex < endIndex, "BEGIN should appear before END");
        Assert.Contains("SELECT", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void EncapsulateBeginEnd_AlreadyWrapped_WrapsAgain()
    {
        // Idempotent wrapping is allowed — wrapping again should produce nested BEGIN END
        const string sql = "BEGIN\r\n    SELECT 1\r\nEND";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        // Should wrap the outer BEGIN...END in another BEGIN...END
        var beginCount = CountOccurrences(result, "BEGIN");
        var endCount   = CountOccurrences(result, "END");

        Assert.True(beginCount >= 2, $"Expected at least 2 BEGINs, got {beginCount}. Result: {result}");
        Assert.True(endCount   >= 2, $"Expected at least 2 ENDs, got {endCount}. Result: {result}");
        Assert.Empty(warnings);
    }

    [Fact]
    public void EncapsulateBeginEnd_MultipleStatements_AllWrapped()
    {
        const string sql = "SELECT 1\r\nSELECT 2\r\nSELECT 3";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Contains("BEGIN", result);
        Assert.Contains("END", result);
        Assert.Contains("SELECT 1", result);
        Assert.Contains("SELECT 2", result);
        Assert.Contains("SELECT 3", result);
        Assert.Empty(warnings);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int idx   = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx++;
        }
        return count;
    }
}
