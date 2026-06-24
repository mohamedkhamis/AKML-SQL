using AkmlSql.Engine.Snippets;
using Xunit;

namespace AkmlSql.Engine.Tests.Snippets;

/// <summary>
/// Spec 030 parity — snippet built-in variable extensions.
/// <list type="bullet">
///   <item>$PASTE$ — SQL Prompt-compatible alias for $CLIPBOARD$ (FR-037).</item>
///   <item>$USER$ — prefers the SQL login name (<see cref="BuiltInVariableContext.SqlUserName"/>)
///   when present; falls back to <see cref="Environment.UserName"/> for integrated-auth
///   connections where UserID is empty.</item>
/// </list>
/// Uses the deterministic <c>Resolve(text, ctx, now)</c> overload where needed so assertions
/// are not sensitive to wall-clock values. Existing <see cref="BuiltInVariableResolverTests"/>
/// are not modified.
/// </summary>
public sealed class Parity_SnippetVariablesTests
{
    private readonly BuiltInVariableResolver _resolver = new();
    private static readonly DateTime FixedNow = new(2026, 06, 24, 12, 00, 00);

    // Helper — builds a context with sensible defaults.
    private static BuiltInVariableContext Ctx(
        string clipboard = "",
        string sqlUserName = "")
    {
        return new BuiltInVariableContext
        {
            DatabaseName = "TestDb",
            ServerName   = "localhost",
            ClipboardText = clipboard,
            SqlUserName   = sqlUserName
        };
    }

    // ── $PASTE$ alias for $CLIPBOARD$ ──────────────────────────────────────

    [Fact]
    public void Paste_ResolvesToClipboardText()
    {
        var result = _resolver.Resolve("$PASTE$", Ctx(clipboard: "pasted value"), FixedNow);
        Assert.Equal("pasted value", result);
    }

    [Fact]
    public void Paste_CaseInsensitive()
    {
        var result = _resolver.Resolve("$paste$", Ctx(clipboard: "hi"), FixedNow);
        Assert.Equal("hi", result);
    }

    [Fact]
    public void Paste_EmptyClipboard_YieldsEmptyString()
    {
        var result = _resolver.Resolve("prefix_$PASTE$_suffix", Ctx(clipboard: ""), FixedNow);
        Assert.Equal("prefix__suffix", result);
    }

    [Fact]
    public void Paste_AndClipboard_BothResolveToSameValue()
    {
        var ctx = Ctx(clipboard: "shared text");
        var withPaste     = _resolver.Resolve("$PASTE$",     ctx, FixedNow);
        var withClipboard = _resolver.Resolve("$CLIPBOARD$", ctx, FixedNow);
        Assert.Equal(withClipboard, withPaste);
    }

    [Fact]
    public void Paste_IsExcludedFromPlaceholderParsing()
    {
        // $PASTE$ must not surface as a custom placeholder — it is a built-in.
        var placeholders = PlaceholderParser.Parse("INSERT $PASTE$ INTO t", []);
        Assert.Empty(placeholders);
    }

    [Fact]
    public void Paste_NotReplacedByReplacePlaceholdersWithDefaults()
    {
        // ReplacePlaceholdersWithDefaults leaves built-ins untouched.
        var result = PlaceholderParser.ReplacePlaceholdersWithDefaults("$PASTE$", []);
        Assert.Equal("$PASTE$", result);
    }

    // ── $USER$ preferring SqlUserName ─────────────────────────────────────

    [Fact]
    public void User_PrefersSqlUserName_WhenNonEmpty()
    {
        var ctx = Ctx(sqlUserName: "sa");
        var result = _resolver.Resolve("$USER$", ctx, FixedNow);
        Assert.Equal("sa", result);
    }

    [Fact]
    public void User_FallsBackToEnvironmentUserName_WhenSqlUserNameEmpty()
    {
        var ctx = Ctx(sqlUserName: "");
        var result = _resolver.Resolve("$USER$", ctx, FixedNow);
        Assert.Equal(Environment.UserName, result);
    }

    [Fact]
    public void User_SqlUserName_CaseInsensitiveToken()
    {
        var ctx = Ctx(sqlUserName: "admin_login");
        var result = _resolver.Resolve("$user$", ctx, FixedNow);
        Assert.Equal("admin_login", result);
    }

    [Fact]
    public void User_SqlUserName_DoesNotAffectOtherVariables()
    {
        var ctx = new BuiltInVariableContext
        {
            DatabaseName  = "Sales",
            ServerName    = "SQL01",
            SqlUserName   = "dbo_user",
            ClipboardText = "clip",
            SelectedText  = "sel"
        };
        var result = _resolver.Resolve("$USER$ / $DATABASE$ / $SERVER$", ctx, FixedNow);
        Assert.Equal("dbo_user / Sales / SQL01", result);
    }

    // ── BuiltInVariableContext defaults ────────────────────────────────────

    [Fact]
    public void BuiltInVariableContext_SqlUserName_DefaultsToEmpty()
    {
        var ctx = new BuiltInVariableContext();
        Assert.Equal(string.Empty, ctx.SqlUserName);
    }
}
