using Xunit;
using AkmlSql.Formatting.Pipeline;

namespace AkmlSql.Formatting.Tests.Pipeline;

public class SqlcmdPreprocessorTests
{
    private readonly SqlcmdPreprocessor _proc = new();

    // ── Preprocess: empty/null ─────────────────────────────────────────────

    [Fact]
    public void Preprocess_Empty_ReturnsEmpty()
    {
        Assert.Equal("", _proc.Preprocess(""));
    }

    [Fact]
    public void Preprocess_Null_ReturnsNull()
    {
        Assert.Null(_proc.Preprocess(null!));
    }

    // ── Preprocess: directives ─────────────────────────────────────────────

    [Fact]
    public void Preprocess_SetVarDirective_ReplacedWithSentinel()
    {
        var sql = ":setvar DatabaseName MyDB\nSELECT 1;";
        var result = _proc.Preprocess(sql);
        Assert.DoesNotContain(":setvar", result);
        Assert.Contains("/*__AKML_SQLCMD_DIRECTIVE_", result);
        Assert.Contains("SELECT 1;", result);
    }

    [Fact]
    public void Preprocess_ConnectDirective_ReplacedWithSentinel()
    {
        var sql = ":connect myserver\nSELECT 1;";
        var result = _proc.Preprocess(sql);
        Assert.DoesNotContain(":connect", result);
        Assert.Contains("/*__AKML_SQLCMD_DIRECTIVE_", result);
    }

    [Fact]
    public void Preprocess_RDirective_ReplacedWithSentinel()
    {
        var sql = ":r C:\\scripts\\setup.sql\nSELECT 1;";
        var result = _proc.Preprocess(sql);
        Assert.DoesNotContain(":r ", result);
    }

    [Fact]
    public void Preprocess_OnErrorDirective_ReplacedWithSentinel()
    {
        var sql = ":on error exit\nSELECT 1;";
        var result = _proc.Preprocess(sql);
        Assert.DoesNotContain(":on error", result);
    }

    [Fact]
    public void Preprocess_XmlDirective_ReplacedWithSentinel()
    {
        var sql = ":xml on\nSELECT 1;";
        var result = _proc.Preprocess(sql);
        Assert.DoesNotContain(":xml", result);
    }

    [Fact]
    public void Preprocess_NoDirectives_ReturnsUnchanged()
    {
        var sql = "SELECT col1 FROM dbo.MyTable WHERE id = 1;";
        var result = _proc.Preprocess(sql);
        Assert.Equal(sql, result);
    }

    // ── Preprocess: inline variables ──────────────────────────────────────

    [Fact]
    public void Preprocess_InlineVariable_ReplacedWithPlaceholder()
    {
        var sql = "SELECT * FROM $(DatabaseName).dbo.Table1;";
        var result = _proc.Preprocess(sql);
        Assert.DoesNotContain("$(DatabaseName)", result);
        Assert.Contains("__AKML_SQLCMD_VAR_DatabaseName__", result);
    }

    [Fact]
    public void Preprocess_SameVariableTwice_UsesConsistentPlaceholder()
    {
        var sql = "SELECT $(Var) AS a, $(Var) AS b;";
        var result = _proc.Preprocess(sql);
        // Both occurrences should use the same placeholder
        var placeholder = "__AKML_SQLCMD_VAR_Var__";
        Assert.Equal(2, result.Split(placeholder).Length - 1); // appears twice
    }

    [Fact]
    public void Preprocess_MultipleVariables_EachReplacedDistinctly()
    {
        var sql = "SELECT $(Db).$(Schema).Table1;";
        var result = _proc.Preprocess(sql);
        Assert.Contains("__AKML_SQLCMD_VAR_Db__", result);
        Assert.Contains("__AKML_SQLCMD_VAR_Schema__", result);
    }

    [Fact]
    public void Preprocess_NoInlineVars_ReturnsUnchanged()
    {
        var sql = "SELECT 1 AS Col;";
        Assert.Equal(sql, _proc.Preprocess(sql));
    }

    // ── Restore ───────────────────────────────────────────────────────────

    [Fact]
    public void Restore_Empty_ReturnsEmpty()
    {
        Assert.Equal("", _proc.Restore(""));
    }

    [Fact]
    public void Restore_Null_ReturnsNull()
    {
        Assert.Null(_proc.Restore(null!));
    }

    [Fact]
    public void Restore_WithoutPreprocess_ReturnsUnchanged()
    {
        var text = "SELECT 1;";
        Assert.Equal(text, _proc.Restore(text));
    }

    // ── Round-trip ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Directive_RestoredFully()
    {
        var original = ":setvar MyVar MyValue\nSELECT 1;";
        var preprocessed = _proc.Preprocess(original);
        var restored = _proc.Restore(preprocessed);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void RoundTrip_InlineVar_RestoredFully()
    {
        var original = "SELECT * FROM $(DatabaseName).dbo.MyTable;";
        var preprocessed = _proc.Preprocess(original);
        var restored = _proc.Restore(preprocessed);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void RoundTrip_Mixed_RestoredFully()
    {
        var original = ":setvar DB MyDB\nSELECT * FROM $(DB).dbo.Orders;";
        var preprocessed = _proc.Preprocess(original);
        Assert.DoesNotContain(":setvar", preprocessed);
        Assert.DoesNotContain("$(DB)", preprocessed);
        var restored = _proc.Restore(preprocessed);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void RoundTrip_MultipleDirectives_RestoredFully()
    {
        var original = ":setvar Server myserver\n:connect $(Server)\nSELECT 1;";
        var preprocessed = _proc.Preprocess(original);
        var restored = _proc.Restore(preprocessed);
        Assert.Equal(original, restored);
    }

    // ── CaseInsensitivity ─────────────────────────────────────────────────

    [Fact]
    public void Preprocess_DirectiveCaseInsensitive()
    {
        var sql = ":SETVAR MyVar 123\nSELECT 1;";
        var result = _proc.Preprocess(sql);
        Assert.DoesNotContain(":SETVAR", result);
        Assert.Contains("/*__AKML_SQLCMD_DIRECTIVE_", result);
    }

    // ── Sequential calls reset state ──────────────────────────────────────

    [Fact]
    public void Preprocess_CalledTwice_ResetsMaps()
    {
        var sql1 = ":setvar A 1\nSELECT 1;";
        var preprocessed1 = _proc.Preprocess(sql1);
        var restored1 = _proc.Restore(preprocessed1);
        Assert.Equal(sql1, restored1);

        // Second call resets state
        var sql2 = ":setvar B 2\nSELECT 2;";
        var preprocessed2 = _proc.Preprocess(sql2);
        var restored2 = _proc.Restore(preprocessed2);
        Assert.Equal(sql2, restored2);
    }
}
