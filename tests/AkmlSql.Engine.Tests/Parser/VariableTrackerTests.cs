using Xunit;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Tests.Parser;

public class VariableTrackerTests
{
    private readonly VariableTracker _tracker = new();

    private static TSqlScript ParseSql(string sql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var script = parser.Parse(reader, out _) as TSqlScript;
        return script!;
    }

    // ── Single DECLARE ────────────────────────────────────────────────────

    [Fact]
    public void TrackVariables_SingleDeclaration_Found()
    {
        var sql = "DECLARE @id INT;\nSELECT @id;";
        var script = ParseSql(sql);

        var vars = _tracker.TrackVariables(script, sql.Length);

        Assert.True(vars.ContainsKey("@id"));
    }

    [Fact]
    public void TrackVariables_SingleDeclaration_TypeRecorded()
    {
        var sql = "DECLARE @id INT;\nSELECT @id;";
        var script = ParseSql(sql);

        var vars = _tracker.TrackVariables(script, sql.Length);

        Assert.Equal("int", vars["@id"]);
    }

    // ── Multiple declarations ─────────────────────────────────────────────

    [Fact]
    public void TrackVariables_MultipleDeclarations_AllFound()
    {
        var sql = "DECLARE @a INT, @b VARCHAR(50);\nSELECT @a, @b;";
        var script = ParseSql(sql);

        var vars = _tracker.TrackVariables(script, sql.Length);

        Assert.True(vars.ContainsKey("@a"));
        Assert.True(vars.ContainsKey("@b"));
    }

    [Fact]
    public void TrackVariables_VarcharType_FormattedWithLength()
    {
        var sql = "DECLARE @name VARCHAR(100);\nSELECT @name;";
        var script = ParseSql(sql);

        var vars = _tracker.TrackVariables(script, sql.Length);

        Assert.True(vars.ContainsKey("@name"));
        Assert.Contains("100", vars["@name"]);
    }

    // ── No variables ──────────────────────────────────────────────────────

    [Fact]
    public void TrackVariables_NoDeclarations_ReturnsEmpty()
    {
        var sql = "SELECT 1;";
        var script = ParseSql(sql);

        var vars = _tracker.TrackVariables(script, sql.Length);

        Assert.Empty(vars);
    }

    // ── Null script ───────────────────────────────────────────────────────

    [Fact]
    public void TrackVariables_NullScript_ReturnsEmpty()
    {
        var vars = _tracker.TrackVariables(null, 0);

        Assert.Empty(vars);
    }

    // ── Cursor offset before declaration ─────────────────────────────────

    [Fact]
    public void TrackVariables_CursorBeforeDeclaration_NotIncluded()
    {
        var sql = "SELECT 1;\nDECLARE @x INT;";
        var script = ParseSql(sql);

        // Cursor at 5 — before the DECLARE
        var vars = _tracker.TrackVariables(script, 5);

        Assert.DoesNotContain("@x", vars.Keys);
    }

    // ── NVarchar MAX ──────────────────────────────────────────────────────

    [Fact]
    public void TrackVariables_NvarcharMax_FormattedCorrectly()
    {
        var sql = "DECLARE @text NVARCHAR(MAX);\nSELECT @text;";
        var script = ParseSql(sql);

        var vars = _tracker.TrackVariables(script, sql.Length);

        Assert.True(vars.ContainsKey("@text"));
        Assert.Contains("MAX", vars["@text"], StringComparison.OrdinalIgnoreCase);
    }
}
