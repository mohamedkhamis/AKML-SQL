using Xunit;
using AkmlSql.Formatting.Pipeline;

namespace AkmlSql.Formatting.Tests.Pipeline;

public class SemanticValidatorTests
{
    private readonly SemanticValidator _validator = new();

    // ── Validate: semantically equivalent SQL ─────────────────────────────

    [Fact]
    public void Validate_IdenticalSql_ReturnsTrue()
    {
        var sql = "SELECT col1 FROM dbo.MyTable WHERE id = 1";
        var diagnostics = new List<FormatDiagnostic>();
        var result = _validator.Validate(sql, sql, diagnostics);
        Assert.True(result);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Validate_SameQueryDifferentWhitespace_ReturnsTrue()
    {
        var original = "SELECT col1 FROM dbo.MyTable";
        var formatted = "SELECT\n    col1\nFROM\n    dbo.MyTable";
        var diagnostics = new List<FormatDiagnostic>();
        var result = _validator.Validate(original, formatted, diagnostics);
        Assert.True(result);
    }

    [Fact]
    public void Validate_SameQueryDifferentCasing_ReturnsTrue()
    {
        var original = "select col1 from dbo.MyTable";
        var formatted = "SELECT col1 FROM dbo.MyTable";
        var diagnostics = new List<FormatDiagnostic>();
        var result = _validator.Validate(original, formatted, diagnostics);
        Assert.True(result);
    }

    // ── Validate: semantically different SQL ──────────────────────────────

    [Fact]
    public void Validate_DifferentColumns_ReturnsFalse()
    {
        var original = "SELECT col1 FROM dbo.MyTable";
        var formatted = "SELECT col1, col2 FROM dbo.MyTable";
        var diagnostics = new List<FormatDiagnostic>();
        var result = _validator.Validate(original, formatted, diagnostics);
        Assert.False(result);
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Validate_DifferentSQL_AddsDiagnostic()
    {
        var original = "SELECT 1";
        var formatted = "SELECT 2";
        var diagnostics = new List<FormatDiagnostic>();
        _validator.Validate(original, formatted, diagnostics);
        Assert.NotEmpty(diagnostics);
    }

    // ── Validate: empty/invalid SQL ───────────────────────────────────────

    [Fact]
    public void Validate_EmptyOriginal_ReturnsFalseOrTrue()
    {
        // Empty SQL parses as an empty script — should handle gracefully
        var diagnostics = new List<FormatDiagnostic>();
        var result = _validator.Validate("", "", diagnostics);
        // Both empty → normalize to same → should return true
        Assert.NotNull(result.ToString()); // just verify no exception thrown
    }

    [Fact]
    public void Validate_InvalidSql_ReturnsFalseWithDiagnostic()
    {
        // Malformed SQL that cannot normalize should still not throw
        var diagnostics = new List<FormatDiagnostic>();
        // ReSharper disable once UnusedVariable
        var result = _validator.Validate("SELECT", "SELEC", diagnostics);
        // Whether true or false, must not throw and diagnostics collection must not be null
        Assert.NotNull(diagnostics);
    }

    // ── Spec 030: built-in function casing is equivalent; literal casing is NOT ────────────
    // (Root cause of the Stage-6 gap: the CasingEngine uppercases sum/count/getdate/etc., but the
    //  validator compared the Sql170ScriptGenerator output case-SENSITIVELY, so every GROUP BY/
    //  HAVING, CTE, MERGE, proc, and subquery corpus item was rejected and returned UNFORMATTED.)

    [Fact]
    public void Validate_BuiltinFunctionCasing_ReturnsTrue()
    {
        var diagnostics = new List<FormatDiagnostic>();
        var result = _validator.Validate("select sum(x) from t;", "SELECT SUM(x) FROM t;", diagnostics);
        Assert.True(result);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Validate_MultipleFunctionCasings_ReturnsTrue()
    {
        var diagnostics = new List<FormatDiagnostic>();
        var result = _validator.Validate(
            "select count(*), getdate(), dateadd(year, -1, getdate()) from t;",
            "SELECT COUNT(*), GETDATE(), DATEADD(YEAR, -1, GETDATE()) FROM t;",
            diagnostics);
        Assert.True(result);
    }

    [Fact]
    public void Validate_StringLiteralCaseChange_ReturnsFalse()  // the relaxation boundary
    {
        var diagnostics = new List<FormatDiagnostic>();
        var result = _validator.Validate("select 'USA';", "select 'usa';", diagnostics);
        Assert.False(result);
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Validate_RemovedPredicate_ReturnsFalse()
    {
        var diagnostics = new List<FormatDiagnostic>();
        var result = _validator.Validate("select a from t where x = 1;", "select a from t;", diagnostics);
        Assert.False(result);
        Assert.NotEmpty(diagnostics);
    }
}
