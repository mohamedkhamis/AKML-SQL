using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Snippets;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.Snippets;

/// <summary>
/// Spec 030 T038 / FR-030 (R7) — snippet expansion by shortcode returns the body with the
/// <c>$CURSOR$</c> marker stripped and its offset reported. Also validates the shipped built-in
/// pack (T041 / FR-031) loads and resolves the expected shortcodes (the same files that ride along
/// in the engine publish output).
/// </summary>
public sealed class SnippetExpandTests : System.IDisposable
{
    private readonly string _personalDir;
    private readonly string _builtInDir;

    public SnippetExpandTests()
    {
        _personalDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"akml_sx_personal_{System.Guid.NewGuid():N}");
        _builtInDir  = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"akml_sx_builtin_{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(_personalDir);
        System.IO.Directory.CreateDirectory(_builtInDir);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_personalDir, true); } catch { }
        try { System.IO.Directory.Delete(_builtInDir, true); } catch { }
    }

    private static void WriteSnippet(string dir, string shortcode, params string[] body)
    {
        var bodyJson = string.Join(", ", body.Select(b => System.Text.Json.JsonSerializer.Serialize(b)));
        var json = $@"{{
  ""metadata"": {{ ""id"": ""t-{shortcode}"", ""shortcode"": ""{shortcode}"", ""name"": ""{shortcode}"",
                  ""description"": """", ""category"": ""Query"", ""context"": [ ""global"" ], ""surroundsWith"": false }},
  ""variables"": [],
  ""body"": [ {bodyJson} ]
}}";
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"{shortcode}.akmlsnippet"), json);
    }

    [Fact]
    public void Expand_ByShortcode_ReturnsBody_AndStripsCursorWithOffset()
    {
        WriteSnippet(_builtInDir, "zzt", "SELECT *", "FROM $CURSOR$");
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir);

        var resp = handler.HandleExpand(new SnippetExpandRequest { Shortcode = "zzt" });

        Assert.True(resp.Success);
        Assert.Equal("SELECT *\nFROM ", resp.ExpandedText);        // body joined by \n, $CURSOR$ removed
        Assert.Equal("SELECT *\nFROM ".Length, resp.CursorOffset);  // caret lands where $CURSOR$ was
    }

    [Fact]
    public void Expand_UnknownShortcode_Fails()
    {
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir);

        var resp = handler.HandleExpand(new SnippetExpandRequest { Shortcode = "no-such-code" });

        Assert.False(resp.Success);
    }

    // ── Selection-range markers $SELECTIONSTART$ / $SELECTIONEND$ (T040 / T047) ──

    [Fact]
    public void Response_RoundTrip_PreservesSelectionOffsets()
    {
        var original = new SnippetExpandResponse
        {
            Success = true,
            ExpandedText = "SELECT 1",
            CursorOffset = 8,
            SelectionStartOffset = 0,
            SelectionEndOffset = 6,
            WasFormatted = true
        };

        var bytes = MessagePackSerializer.Serialize(original);
        var copy = MessagePackSerializer.Deserialize<SnippetExpandResponse>(bytes);

        Assert.Equal(original.Success, copy.Success);
        Assert.Equal(original.ExpandedText, copy.ExpandedText);
        Assert.Equal(original.CursorOffset, copy.CursorOffset);
        Assert.Equal(0, copy.SelectionStartOffset);
        Assert.Equal(6, copy.SelectionEndOffset);
        Assert.Equal(original.WasFormatted, copy.WasFormatted);
    }

    [Fact]
    public void Response_DefaultSelectionOffsets_AreMinusOne()
    {
        // A fresh response (e.g. the failure path) must serialize -1, not 0, for both selection fields.
        var resp = new SnippetExpandResponse { Success = false };

        var copy = MessagePackSerializer.Deserialize<SnippetExpandResponse>(
            MessagePackSerializer.Serialize(resp));

        Assert.Equal(-1, copy.SelectionStartOffset);
        Assert.Equal(-1, copy.SelectionEndOffset);
    }

    [Fact]
    public void Expand_SelectionMarkers_ReportOffsets_AndStripMarkers()
    {
        // Body laid out as a single line so offsets are easy to reason about:
        //   $SELECTIONSTART$SELECT$SELECTIONEND$ $CURSOR$  →  "SELECT "
        WriteSnippet(_builtInDir, "zsel", "$SELECTIONSTART$SELECT$SELECTIONEND$ $CURSOR$");
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir);

        var resp = handler.HandleExpand(new SnippetExpandRequest { Shortcode = "zsel" });

        Assert.True(resp.Success);
        Assert.Equal("SELECT ", resp.ExpandedText);
        Assert.DoesNotContain("$SELECTIONSTART$", resp.ExpandedText);
        Assert.DoesNotContain("$SELECTIONEND$", resp.ExpandedText);
        Assert.DoesNotContain("$CURSOR$", resp.ExpandedText);
        Assert.Equal(0, resp.SelectionStartOffset); // selection begins at start of "SELECT"
        Assert.Equal(6, resp.SelectionEndOffset);   // selection ends after "SELECT"
        Assert.Equal(7, resp.CursorOffset);          // caret lands after the trailing space
    }

    [Fact]
    public void Expand_NoSelectionMarkers_OffsetsAreMinusOne()
    {
        WriteSnippet(_builtInDir, "znosel", "SELECT *", "FROM $CURSOR$");
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir);

        var resp = handler.HandleExpand(new SnippetExpandRequest { Shortcode = "znosel" });

        Assert.True(resp.Success);
        Assert.Equal(-1, resp.SelectionStartOffset);
        Assert.Equal(-1, resp.SelectionEndOffset);
        Assert.Equal("SELECT *\nFROM ".Length, resp.CursorOffset); // unchanged $CURSOR$ behavior
    }

    [Fact]
    public void Expand_SelectionMarkers_WithoutCursor_ClampCursorToEnd()
    {
        // No $CURSOR$ → caret clamps to end-of-text; selection offsets must still be exact.
        WriteSnippet(_builtInDir, "zselnc", "($SELECTIONSTART$x$SELECTIONEND$)");
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir);

        var resp = handler.HandleExpand(new SnippetExpandRequest { Shortcode = "zselnc" });

        Assert.True(resp.Success);
        Assert.Equal("(x)", resp.ExpandedText);
        Assert.Equal(1, resp.SelectionStartOffset); // after the '('
        Assert.Equal(2, resp.SelectionEndOffset);   // after the 'x'
        Assert.Equal(3, resp.CursorOffset);          // clamped to end of "(x)"
    }

    // ── Shipped built-in pack (T041 / FR-031) ──

    [Theory]
    [InlineData("ssf")]
    [InlineData("sel")]
    [InlineData("ins")]
    [InlineData("upd")]
    [InlineData("del")]
    [InlineData("cte")]
    public void ShippedPack_ResolvesExpectedShortcode(string shortcode)
    {
        var packDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "snippets");
        Assert.True(System.IO.Directory.Exists(packDir), $"built-in snippet pack folder missing: {packDir}");

        var handler = new SnippetRequestHandler(_personalDir, packDir);
        var resp = handler.HandleExpand(new SnippetExpandRequest { Shortcode = shortcode });

        Assert.True(resp.Success, $"shipped pack should resolve '{shortcode}'");
        Assert.False(string.IsNullOrWhiteSpace(resp.ExpandedText));
        // CursorOffset is always >= 0 on success (HandleExpand clamps a missing marker to end-of-text),
        // so assert the $CURSOR$ marker on the SOURCE file to actually guard caret-positioning intent.
        var raw = System.IO.File.ReadAllText(System.IO.Path.Combine(packDir, shortcode + ".akmlsnippet"));
        Assert.Contains("$CURSOR$", raw);
    }
}
