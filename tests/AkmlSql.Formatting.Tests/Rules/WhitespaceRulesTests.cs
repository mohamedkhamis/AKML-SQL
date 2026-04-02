using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Rules;

public class WhitespaceRulesTests
{
    private readonly WhitespaceRules _rules = new();

    private static LayoutNode Node(
        string text,
        TSqlTokenType tokenType = TSqlTokenType.Identifier,
        BreakType breakType = BreakType.None,
        int spaces = 1,
        int indent = 0,
        bool inNoformat = false)
    {
        return new LayoutNode
        {
            FormattedText = text,
            TokenType = tokenType,
            PrecedingBreak = breakType,
            PrecedingSpaces = spaces,
            IndentLevel = indent,
            IsInNoformatRegion = inNoformat
        };
    }

    // ── SpaceAfterComma ───────────────────────────────────────────────────

    [Fact]
    public void Apply_SpaceAfterComma_True_SetsOneSpace()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                SpaceAfterComma = true,
                LineBreakAfterComma = false // disable so spacing rule actually runs
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1"),
            Node(",", TSqlTokenType.Comma, spaces: 0),
            Node("col2", spaces: 0) // no space initially
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(1, nodes[2].PrecedingSpaces);
    }

    [Fact]
    public void Apply_SpaceAfterComma_False_SetsZeroSpace()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                SpaceAfterComma = false,
                LineBreakAfterComma = false // disable so spacing rule actually runs
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1"),
            Node(",", TSqlTokenType.Comma, spaces: 0),
            Node("col2", spaces: 1)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(0, nodes[2].PrecedingSpaces);
    }

    // ── No space before comma ─────────────────────────────────────────────

    [Fact]
    public void Apply_NoSpaceBeforeComma()
    {
        var profile = new FormattingProfile();
        var nodes = new List<LayoutNode>
        {
            Node("col1"),
            Node(",", TSqlTokenType.Comma, spaces: 1) // has space
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(0, nodes[1].PrecedingSpaces);
    }

    // ── No space before semicolon ─────────────────────────────────────────

    [Fact]
    public void Apply_NoSpaceBeforeSemicolon()
    {
        var profile = new FormattingProfile();
        var nodes = new List<LayoutNode>
        {
            Node("SELECT 1"),
            Node(";", TSqlTokenType.Semicolon, spaces: 2)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(0, nodes[1].PrecedingSpaces);
    }

    // ── No space around dot ───────────────────────────────────────────────

    [Fact]
    public void Apply_NoSpaceAroundDot()
    {
        var profile = new FormattingProfile();
        var nodes = new List<LayoutNode>
        {
            Node("dbo"),
            Node(".", TSqlTokenType.Dot, spaces: 1),
            Node("MyTable", spaces: 1)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(0, nodes[1].PrecedingSpaces);
        Assert.Equal(0, nodes[2].PrecedingSpaces);
    }

    // ── SpaceAroundOperators ──────────────────────────────────────────────

    [Fact]
    public void Apply_SpaceAroundOperators_True_EnsuresSpace()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                SpaceAroundOperators = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1"),
            Node("=", TSqlTokenType.EqualsSign, spaces: 0),
            Node("1", spaces: 0)
        };

        _rules.Apply(nodes, profile);

        Assert.True(nodes[1].PrecedingSpaces >= 1);
        Assert.True(nodes[2].PrecedingSpaces >= 1);
    }

    [Fact]
    public void Apply_SpaceAroundOperators_False_RemovesSpace()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                SpaceAroundOperators = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1"),
            Node("=", TSqlTokenType.EqualsSign, spaces: 1),
            Node("1", spaces: 1)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(0, nodes[1].PrecedingSpaces);
        Assert.Equal(0, nodes[2].PrecedingSpaces);
    }

    // ── SpaceInsideParentheses ────────────────────────────────────────────

    [Fact]
    public void Apply_SpaceInsideParentheses_True_AddsSpace()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                SpaceInsideParentheses = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("(", TSqlTokenType.LeftParenthesis, spaces: 0),
            Node("a", spaces: 0), // no space after (
            Node(")", TSqlTokenType.RightParenthesis, spaces: 0)
        };

        _rules.Apply(nodes, profile);

        Assert.True(nodes[1].PrecedingSpaces >= 1); // space after (
        Assert.True(nodes[2].PrecedingSpaces >= 1); // space before )
    }

    [Fact]
    public void Apply_SpaceInsideParentheses_False_RemovesSpace()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                SpaceInsideParentheses = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("a", spaces: 1) // has space after (
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(0, nodes[1].PrecedingSpaces);
    }

    // ── LineBreakAfterSemicolon ───────────────────────────────────────────

    [Fact]
    public void Apply_LineBreakAfterSemicolon_True_AddsBreak()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                LineBreakAfterSemicolon = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node(";", TSqlTokenType.Semicolon),
            Node("SELECT", TSqlTokenType.Select, spaces: 1)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
    }

    // ── GO whitespace ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyLineBeforeGO_True_PromotesToEmptyLine()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                EmptyLineBeforeGo = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("SELECT 1"),
            Node("GO", TSqlTokenType.Go, BreakType.NewLine)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.EmptyLine, nodes[1].PrecedingBreak);
    }

    [Fact]
    public void Apply_EmptyLineBeforeGO_False_ReducesToNewLine()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                EmptyLineBeforeGo = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("SELECT 1"),
            Node("GO", TSqlTokenType.Go, BreakType.EmptyLine)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
    }

    // ── MaxConsecutiveEmptyLines ──────────────────────────────────────────

    [Fact]
    public void Apply_MaxConsecutiveEmptyLines_ReducesExcess()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                MaxConsecutiveEmptyLines = 1,
                PreserveEmptyLines = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("SELECT 1"),
            Node("", breakType: BreakType.EmptyLine, spaces: 0),
            Node("", breakType: BreakType.EmptyLine, spaces: 0), // second consecutive empty line — exceeds limit
            Node("SELECT 2")
        };

        _rules.Apply(nodes, profile);

        // Second empty line (nodes[2]) should be demoted to NewLine
        Assert.Equal(BreakType.NewLine, nodes[2].PrecedingBreak);
    }

    // ── LineBreakBeforeComma ──────────────────────────────────────────────

    [Fact]
    public void Apply_LineBreakBeforeComma_True_AddsBreakBeforeComma()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                LineBreakBeforeComma = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1"),
            Node(",", TSqlTokenType.Comma) // inline comma
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
    }

    // ── NoformatRegion is skipped ─────────────────────────────────────────

    [Fact]
    public void Apply_NoformatRegion_CommaBreak_Skipped()
    {
        // In noformat region, the node itself should not have LineBreakBeforeComma applied
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                LineBreakBeforeComma = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1"),
            Node(",", TSqlTokenType.Comma, inNoformat: true) // in noformat region
        };

        _rules.Apply(nodes, profile);

        // The comma is in noformat, so LineBreakBeforeComma should not change it
        Assert.Equal(BreakType.None, nodes[1].PrecedingBreak);
    }

    // ── FinalNewline = "remove" ───────────────────────────────────────────

    [Fact]
    public void Apply_FinalNewline_Remove_RemovesTrailingEmptyLine()
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
            new() { FormattedText = "", PrecedingBreak = BreakType.EmptyLine, PrecedingSpaces = 0, TokenType = TSqlTokenType.WhiteSpace }
        };

        _rules.Apply(nodes, profile);

        // Trailing empty-line node should be removed
        Assert.Single(nodes);
    }

    // ── EmptyLine: SpaceAroundBooleanOperators ────────────────────────────

    [Fact]
    public void Apply_SpaceAroundBooleanOperators_True_EnsuresSpace()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                SpaceAroundBooleanOperators = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1 = 1"),
            Node("AND", TSqlTokenType.And, spaces: 0),
            Node("col2 = 2", spaces: 0)
        };

        _rules.Apply(nodes, profile);

        Assert.True(nodes[1].PrecedingSpaces >= 1);
        Assert.True(nodes[2].PrecedingSpaces >= 1);
    }
}
