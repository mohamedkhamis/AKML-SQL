using Xunit;
using AkmlSql.Engine.Snippets;

namespace AkmlSql.Engine.Tests.Snippets;

public class BuiltInVariableResolverTests
{
    private readonly BuiltInVariableResolver _resolver = new();

    private static BuiltInVariableContext Context(
        string db = "TestDb", string server = "localhost",
        string schema = "dbo", string file = "Query.sql",
        string clipboard = "clip", string selected = "sel")
    {
        return new BuiltInVariableContext
        {
            DatabaseName = db,
            ServerName = server,
            SchemaName = schema,
            FileName = file,
            ClipboardText = clipboard,
            SelectedText = selected
        };
    }

    [Fact]
    public void Resolve_Date_Replaced()
    {
        var result = _resolver.Resolve("$DATE$", Context());
        Assert.DoesNotContain("$DATE$", result);
        // Verify format yyyy-MM-dd
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", result);
    }

    [Fact]
    public void Resolve_DateTime_Replaced()
    {
        var result = _resolver.Resolve("$DATETIME$", Context());
        Assert.DoesNotContain("$DATETIME$", result);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", result);
    }

    [Fact]
    public void Resolve_Time_Replaced()
    {
        var result = _resolver.Resolve("$TIME$", Context());
        Assert.DoesNotContain("$TIME$", result);
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", result);
    }

    [Fact]
    public void Resolve_User_Replaced()
    {
        var result = _resolver.Resolve("$USER$", Context());
        Assert.DoesNotContain("$USER$", result);
        Assert.Equal(Environment.UserName, result);
    }

    [Fact]
    public void Resolve_Machine_Replaced()
    {
        var result = _resolver.Resolve("$MACHINE$", Context());
        Assert.DoesNotContain("$MACHINE$", result);
        Assert.Equal(Environment.MachineName, result);
    }

    [Fact]
    public void Resolve_Database_Replaced()
    {
        var result = _resolver.Resolve("$DATABASE$", Context(db: "SalesDB"));
        Assert.Equal("SalesDB", result);
    }

    [Fact]
    public void Resolve_Server_Replaced()
    {
        var result = _resolver.Resolve("$SERVER$", Context(server: "SQLSRV01"));
        Assert.Equal("SQLSRV01", result);
    }

    [Fact]
    public void Resolve_Schema_Replaced()
    {
        var result = _resolver.Resolve("$SCHEMA$", Context(schema: "sales"));
        Assert.Equal("sales", result);
    }

    [Fact]
    public void Resolve_Schema_DefaultsToDboWhenEmpty()
    {
        var result = _resolver.Resolve("$SCHEMA$", Context(schema: ""));
        Assert.Equal("dbo", result);
    }

    [Fact]
    public void Resolve_Guid_Replaced()
    {
        var result = _resolver.Resolve("$GUID$", Context());
        Assert.DoesNotContain("$GUID$", result);
        Assert.True(Guid.TryParse(result, out _), $"Expected GUID but got: {result}");
    }

    [Fact]
    public void Resolve_Year_Replaced()
    {
        var result = _resolver.Resolve("$YEAR$", Context());
        Assert.Equal(DateTime.Now.Year.ToString(), result);
    }

    [Fact]
    public void Resolve_FileName_Replaced()
    {
        var result = _resolver.Resolve("$FILENAME$", Context(file: "MyQuery.sql"));
        Assert.Equal("MyQuery.sql", result);
    }

    [Fact]
    public void Resolve_Clipboard_Replaced()
    {
        var result = _resolver.Resolve("$CLIPBOARD$", Context(clipboard: "copied text"));
        Assert.Equal("copied text", result);
    }

    [Fact]
    public void Resolve_SelectedText_Replaced()
    {
        var result = _resolver.Resolve("$SELECTEDTEXT$", Context(selected: "my selection"));
        Assert.Equal("my selection", result);
    }

    [Fact]
    public void Resolve_CursorNotReplaced()
    {
        // $CURSOR$ is handled separately by PlaceholderParser
        var result = _resolver.Resolve("$CURSOR$", Context());
        Assert.Equal("$CURSOR$", result); // untouched
    }

    [Fact]
    public void Resolve_CaseInsensitive()
    {
        var result = _resolver.Resolve("$database$", Context(db: "TestDb"));
        Assert.Equal("TestDb", result);
    }

    [Fact]
    public void Resolve_MultipleVariables()
    {
        var result = _resolver.Resolve("-- DB: $DATABASE$ / Server: $SERVER$",
            Context(db: "SalesDB", server: "SQL01"));
        Assert.Equal("-- DB: SalesDB / Server: SQL01", result);
    }

    [Fact]
    public void BuiltInVariableContext_Defaults()
    {
        var ctx = new BuiltInVariableContext();
        Assert.Equal(string.Empty, ctx.DatabaseName);
        Assert.Equal(string.Empty, ctx.ServerName);
        Assert.Equal(string.Empty, ctx.SchemaName);
        Assert.Equal(string.Empty, ctx.FileName);
        Assert.Equal(string.Empty, ctx.ClipboardText);
        Assert.Equal(string.Empty, ctx.SelectedText);
    }

    // ── Spec 030 T047 / FR-037: custom $DATE(fmt)$ / $TIME(fmt)$ / $DATETIME(fmt)$ formats.
    //    Uses the deterministic `now` overload so the assertions don't race the clock. ──

    private static readonly DateTime FixedNow = new(2026, 06, 14, 09, 05, 07);

    [Fact]
    public void CustomDateFormat_Applies()
        => Assert.Equal("2026", _resolver.Resolve("$DATE(yyyy)$", Context(), FixedNow));

    [Fact]
    public void CustomTimeFormat_Applies()
        => Assert.Equal("09:05", _resolver.Resolve("$TIME(HH:mm)$", Context(), FixedNow));

    [Fact]
    public void CustomDateTimeFormat_Applies()
        => Assert.Equal("2026-06-14 09:05",
            _resolver.Resolve("$DATETIME(yyyy-MM-dd HH:mm)$", Context(), FixedNow));

    [Fact]
    public void CustomFormat_TokenIsCaseInsensitive_FormatCasingPreserved()
        => Assert.Equal("2026", _resolver.Resolve("$date(yyyy)$", Context(), FixedNow));

    [Fact]
    public void FixedDateToken_StillWorks_AlongsideCustom()
        => Assert.Equal("2026-06-14", _resolver.Resolve("$DATE$", Context(), FixedNow));

    [Fact]
    public void EmptyCustomFormat_FallsBackToDefault()
        => Assert.Equal("2026-06-14", _resolver.Resolve("$DATE()$", Context(), FixedNow));

    [Fact]
    public void InvalidCustomFormat_FallsBackToDefault()
        => Assert.Equal("2026-06-14", _resolver.Resolve("$DATE(%)$", Context(), FixedNow));

    [Fact]
    public void MixedTokens_AllResolve()
        => Assert.Equal("y=2026 t=09:05 d=2026-06-14",
            _resolver.Resolve("y=$DATE(yyyy)$ t=$TIME(HH:mm)$ d=$DATE$", Context(), FixedNow));
}
