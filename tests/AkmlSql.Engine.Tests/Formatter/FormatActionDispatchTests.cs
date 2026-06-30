using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Engine.Tests.Formatter;

/// <summary>
/// Spec 030 (T015/T016) — the standalone format-action dispatch. Actions 0–8 (the IFormatAction
/// classes) were never dispatched by HandleFormatAction (only 9–17 were); the shell already sends
/// these action types, so they returned "not supported here". These tests pin the wired behaviour.
/// Schema-dependent actions (ExpandWildcards=3, QualifyObjectNames=4) are now schema-aware
/// lightweight operations (spec 030); under this no-session dispatch (one-arg HandleFormatAction)
/// they have no schema cache, so they return a clear "schema cache not available" warning rather
/// than transforming. The transforming behaviour is covered by the Parity3_* operation tests.
/// </summary>
public class FormatActionDispatchTests : IDisposable
{
    private readonly string _builtInDir;
    private readonly string _customDir;
    private readonly FormatRequestHandler _handler;

    public FormatActionDispatchTests()
    {
        _builtInDir = Path.Combine(Path.GetTempPath(), $"akml_fa_builtin_{Guid.NewGuid():N}");
        _customDir = Path.Combine(Path.GetTempPath(), $"akml_fa_custom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);
        _handler = new FormatRequestHandler(new ProfileManager(_builtInDir, _customDir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_builtInDir, recursive: true); } catch { }
        try { Directory.Delete(_customDir, recursive: true); } catch { }
    }

    private FormatActionResponse Run(FormatActionType type, string sql) =>
        _handler.HandleFormatAction(new FormatActionRequest { Text = sql, ActionType = (int)type, ProfileName = null });

    [Fact]
    public void CasingOnly_UppercasesKeywords()
    {
        var r = Run(FormatActionType.CasingOnly, "select 1");
        Assert.True(r.Success);
        Assert.Contains("SELECT", r.FormattedText);
    }

    [Fact]
    public void InsertSemicolons_AddsTerminator()
    {
        var r = Run(FormatActionType.InsertSemicolons, "SELECT 1");
        Assert.True(r.Success);
        Assert.True(r.WasModified);
        Assert.Contains(";", r.FormattedText);
    }

    [Fact]
    public void RemoveSemicolons_StripsTerminator()
    {
        var r = Run(FormatActionType.RemoveSemicolons, "SELECT 1;");
        Assert.True(r.Success);
        Assert.DoesNotContain(";", r.FormattedText);
    }

    [Fact]
    public void AddSquareBrackets_WrapsIdentifiers()
    {
        var r = Run(FormatActionType.AddSquareBrackets, "SELECT a FROM t");
        Assert.True(r.Success);
        Assert.Contains("[a]", r.FormattedText);
        Assert.Contains("[t]", r.FormattedText);
    }

    [Fact]
    public void RemoveSquareBrackets_UnwrapsSimpleIdentifiers()
    {
        var r = Run(FormatActionType.RemoveSquareBrackets, "SELECT [a] FROM [t]");
        Assert.True(r.Success);
        Assert.DoesNotContain("[a]", r.FormattedText);
        Assert.DoesNotContain("[t]", r.FormattedText);
    }

    [Fact]
    public void RemoveAsKeyword_RemovesAliasAs()
    {
        var r = Run(FormatActionType.RemoveAsKeyword, "SELECT x AS y FROM t");
        Assert.True(r.Success);
        Assert.DoesNotContain(" AS ", r.FormattedText);
    }

    [Fact]
    public void ExpandWildcards_NoSchemaCache_ReturnsClearSchemaWarning()
    {
        // One-arg dispatch has no SessionManager/SchemaCacheManager → no cache → graceful warning.
        var r = Run(FormatActionType.ExpandWildcards, "SELECT * FROM t");
        Assert.True(r.Success);                 // does not error
        Assert.False(r.WasModified);            // and does not transform without a cache
        Assert.NotNull(r.Warnings);
        Assert.Contains(r.Warnings!, w => w.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QualifyObjectNames_NoSchemaCache_ReturnsClearSchemaWarning()
    {
        var r = Run(FormatActionType.QualifyObjectNames, "SELECT a FROM t");
        Assert.True(r.Success);
        Assert.False(r.WasModified);
        Assert.NotNull(r.Warnings);
        Assert.Contains(r.Warnings!, w => w.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }
}
