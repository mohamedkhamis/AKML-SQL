using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Rules;

public class ParenthesisRulesTests
{
    private readonly ParenthesisRules _rules = new();

    private static LayoutNode Node(
        string text,
        TSqlTokenType tokenType = TSqlTokenType.Identifier,
        BreakType breakType = BreakType.None,
        int spaces = 1,
        int indent = 0) => new()
    {
        FormattedText = text,
        TokenType = tokenType,
        PrecedingBreak = breakType,
        PrecedingSpaces = spaces,
        IndentLevel = indent
    };

    // ── SpaceInside ───────────────────────────────────────────────────────

    [Fact]
    public void Apply_SpaceInside_True_AddsSpaceAfterOpenParen()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                SpaceInside = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("a", spaces: 0), // no space after (
            Node(")", TSqlTokenType.RightParenthesis, spaces: 0)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(1, nodes[1].PrecedingSpaces); // space after (
        Assert.Equal(1, nodes[2].PrecedingSpaces); // space before )
    }

    [Fact]
    public void Apply_SpaceInside_False_RemovesSpace()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                SpaceInside = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("a", spaces: 1), // has space
            Node(")", TSqlTokenType.RightParenthesis, spaces: 1)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(0, nodes[1].PrecedingSpaces);
        Assert.Equal(0, nodes[2].PrecedingSpaces);
    }

    // ── CloseOnNewLine ────────────────────────────────────────────────────

    [Fact]
    public void Apply_CloseOnNewLine_True_AddsBreakBeforeCloseParen()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                CloseOnNewLine = "true",
                CollapseShort = false   // prevent collapse from undoing the break
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("a"),
            Node(")", TSqlTokenType.RightParenthesis, spaces: 0) // inline )
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[2].PrecedingBreak);
    }

    // ── CollapseShort ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_CollapseShort_True_ShortParenCollapsed()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                CollapseShort = true,
                CollapseThreshold = 100
            }
        };

        // ( a , b ) with breaks
        var nodes = new List<LayoutNode>
        {
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("a", breakType: BreakType.NewLine, spaces: 0),
            Node(",", TSqlTokenType.Comma),
            Node("b", breakType: BreakType.NewLine, spaces: 0),
            Node(")", TSqlTokenType.RightParenthesis, breakType: BreakType.NewLine, spaces: 0)
        };

        _rules.Apply(nodes, profile);

        // Breaks should be collapsed
        Assert.Equal(BreakType.None, nodes[1].PrecedingBreak);
        Assert.Equal(BreakType.None, nodes[3].PrecedingBreak);
        Assert.Equal(BreakType.None, nodes[4].PrecedingBreak);
    }

    [Fact]
    public void Apply_CollapseShort_True_SubqueryNotCollapsed()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                CollapseShort = true,
                CollapseThreshold = 100
            }
        };

        // ( SELECT 1 ) — contains subquery
        var nodes = new List<LayoutNode>
        {
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("SELECT", TSqlTokenType.Select, breakType: BreakType.NewLine),
            Node("1"),
            Node(")", TSqlTokenType.RightParenthesis, breakType: BreakType.NewLine)
        };

        _rules.Apply(nodes, profile);

        // SubQuery guard — should NOT be collapsed
        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
    }

    [Fact]
    public void Apply_CollapseShort_False_BreaksPreserved()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                CollapseShort = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("a", breakType: BreakType.NewLine, spaces: 0),
            Node(")", TSqlTokenType.RightParenthesis, breakType: BreakType.NewLine, spaces: 0)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
    }

    // ── OpenOnSameLine ────────────────────────────────────────────────────

    [Fact]
    public void Apply_OpenOnSameLine_True_MovesBrokenParenInline()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                OpenOnSameLine = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("SELECT"),
            Node("(", TSqlTokenType.LeftParenthesis, BreakType.NewLine), // open paren on new line
            Node("a"),
            Node(")", TSqlTokenType.RightParenthesis)
        };

        _rules.Apply(nodes, profile);

        // Should be moved inline
        Assert.Equal(BreakType.None, nodes[1].PrecedingBreak);
    }

    // ── Empty parens ──────────────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyParens_NoThrow()
    {
        var profile = new FormattingProfile();

        var nodes = new List<LayoutNode>
        {
            Node("GETDATE"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node(")", TSqlTokenType.RightParenthesis)
        };

        var ex = Record.Exception(() => _rules.Apply(nodes, profile));
        Assert.Null(ex);
    }

    // ── Empty list ─────────────────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyList_NoThrow()
    {
        var profile = new FormattingProfile();
        var nodes = new List<LayoutNode>();
        var ex = Record.Exception(() => _rules.Apply(nodes, profile));
        Assert.Null(ex);
    }

    // ── Nested parens ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_NestedParens_NoThrow()
    {
        var profile = new FormattingProfile();

        var nodes = new List<LayoutNode>
        {
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("a"),
            Node("+"),
            Node("b"),
            Node(")", TSqlTokenType.RightParenthesis),
            Node(")", TSqlTokenType.RightParenthesis)
        };

        var ex = Record.Exception(() => _rules.Apply(nodes, profile));
        Assert.Null(ex);
    }
}
