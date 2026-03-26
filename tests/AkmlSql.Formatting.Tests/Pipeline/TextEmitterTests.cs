using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;
// ReSharper disable once RedundantUsingDirective

namespace AkmlSql.Formatting.Tests.Pipeline;

public class TextEmitterTests
{
    private readonly TextEmitter _emitter = new();

    // Helper profile that disables FinalNewline and TrailingWhitespace so tests can assert exact strings
    private static FormattingProfile FlatProfile(string? tabStyle = null, int tabSize = 4) => new()
    {
        Whitespace =
        {
            FinalNewline = "none",
            TrailingWhitespace = "keep",
            TabStyle = tabStyle ?? "spaces",
            TabSize = tabSize
        }
    };

    private static LayoutNode Node(
        string text,
        TSqlTokenType tokenType = TSqlTokenType.Identifier,
        BreakType breakType = BreakType.None,
        int spaces = 0,
        int indent = 0,
        string? trailingComment = null,
        string? originalText = null) => new()
    {
        FormattedText = text,
        OriginalText = originalText ?? text,
        TokenType = tokenType,
        PrecedingBreak = breakType,
        PrecedingSpaces = spaces,
        IndentLevel = indent,
        TrailingComment = trailingComment == null ? null : new CommentAttachment { Text = trailingComment }
    };

    // ── Single node ───────────────────────────────────────────────────────

    [Fact]
    public void Emit_SingleNode_ProducesText()
    {
        var nodes = new List<LayoutNode> { Node("SELECT") };

        string result = _emitter.Emit(nodes, FlatProfile());

        Assert.Equal("SELECT", result);
    }

    // ── Spacing ───────────────────────────────────────────────────────────

    [Fact]
    public void Emit_TwoNodes_SpacesBetween()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT"),
            Node("1", spaces: 1)
        };

        string result = _emitter.Emit(nodes, FlatProfile());

        Assert.Equal("SELECT 1", result);
    }

    [Fact]
    public void Emit_ZeroSpaces_NoSpace()
    {
        var nodes = new List<LayoutNode>
        {
            Node("dbo"),
            Node(".", TSqlTokenType.Dot, spaces: 0),
            Node("MyTable", spaces: 0)
        };

        string result = _emitter.Emit(nodes, FlatProfile());

        Assert.Equal("dbo.MyTable", result);
    }

    // ── Line breaks ───────────────────────────────────────────────────────

    [Fact]
    public void Emit_NewLine_InsertsNewlineCharacter()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT"),
            Node("FROM", TSqlTokenType.From, BreakType.NewLine, spaces: 0)
        };

        string result = _emitter.Emit(nodes, FlatProfile());

        Assert.Contains("\n", result);
        Assert.Equal("SELECT\nFROM", result);
    }

    [Fact]
    public void Emit_EmptyLine_InsertsBlankLine()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT 1"),
            Node("SELECT 2", breakType: BreakType.EmptyLine, spaces: 0)
        };

        string result = _emitter.Emit(nodes, FlatProfile());

        // EmptyLine = two newline characters
        Assert.Equal("SELECT 1\n\nSELECT 2", result);
    }

    // ── Indentation ───────────────────────────────────────────────────────

    [Fact]
    public void Emit_IndentLevel1_FourSpaces()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT"),
            Node("col", breakType: BreakType.NewLine, spaces: 0, indent: 1)
        };

        string result = _emitter.Emit(nodes, FlatProfile("spaces", 4));

        Assert.Equal("SELECT\n    col", result);
    }

    [Fact]
    public void Emit_IndentLevel1_Tab()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT"),
            Node("col", breakType: BreakType.NewLine, spaces: 0, indent: 1)
        };

        string result = _emitter.Emit(nodes, FlatProfile("tabs", 4));

        Assert.Equal("SELECT\n\tcol", result);
    }

    [Fact]
    public void Emit_IndentLevel2_EightSpaces()
    {
        var nodes = new List<LayoutNode>
        {
            Node("BEGIN"),
            Node("SELECT", breakType: BreakType.NewLine, spaces: 0, indent: 2)
        };

        string result = _emitter.Emit(nodes, FlatProfile("spaces", 4));

        Assert.Equal("BEGIN\n        SELECT", result);
    }

    // ── Empty text nodes ──────────────────────────────────────────────────

    [Fact]
    public void Emit_EmptyTextNode_Skipped()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT"),
            Node("", spaces: 0),  // empty node — should not add space
            Node("1", spaces: 1)
        };

        string result = _emitter.Emit(nodes, FlatProfile());

        Assert.Equal("SELECT 1", result);
    }

    // ── Trailing newline ──────────────────────────────────────────────────

    [Fact]
    public void Emit_FinalNewline_Ensure_AddsNewline()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                FinalNewline = "ensure"
            }
        };

        var nodes = new List<LayoutNode> { Node("SELECT 1") };

        string result = _emitter.Emit(nodes, profile);

        Assert.EndsWith("\n", result);
    }

    [Fact]
    public void Emit_FinalNewline_Remove_RemovesNewline()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                FinalNewline = "remove"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("SELECT 1"),
            Node("", breakType: BreakType.NewLine, spaces: 0)
        };

        string result = _emitter.Emit(nodes, profile);

        Assert.False(result.EndsWith("\n"), "Should not end with newline when FinalNewline=remove");
    }

    // ── Trailing whitespace ───────────────────────────────────────────────

    [Fact]
    public void Emit_TrailingWhitespace_Remove_TrimsLines()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                TrailingWhitespace = "remove"
            }
        };

        // First line has trailing spaces (3 spaces before newline)
        var nodes = new List<LayoutNode>
        {
            Node("SELECT"),
            Node("   ", spaces: 0),  // trailing spaces
            Node("1", breakType: BreakType.NewLine, spaces: 0)
        };

        string result = _emitter.Emit(nodes, profile);

        // Each line should not end with spaces
        var lines = result.Split('\n');
        foreach (var line in lines)
        {
            Assert.False(line.EndsWith(" "), $"Line should not end with space: '{line}'");
        }
    }

    // ── Empty list ─────────────────────────────────────────────────────────

    [Fact]
    public void Emit_EmptyList_ReturnsEmpty()
    {
        var profile = new FormattingProfile();
        var nodes = new List<LayoutNode>();

        string result = _emitter.Emit(nodes, profile);

        Assert.Equal("", result);
    }

    // ── Trailing comment ──────────────────────────────────────────────────

    [Fact]
    public void Emit_TrailingComment_AppendedAfterToken()
    {
        var profile = new FormattingProfile();
        var nodes = new List<LayoutNode>
        {
            Node("SELECT 1", trailingComment: "-- my comment")
        };

        string result = _emitter.Emit(nodes, profile);

        Assert.Contains("-- my comment", result);
        Assert.StartsWith("SELECT 1 ", result);
    }
}
