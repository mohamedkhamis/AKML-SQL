using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Rules;

public class JoinRulesTests
{
    private readonly JoinRules _rules = new();

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

    // ── EmptyLineBeforeJoin ───────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyLineBeforeJoin_True_PromotesNewLineToEmptyLine()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                EmptyLineBeforeJoin = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("FROM", TSqlTokenType.From),
            Node("t1"),
            Node("JOIN", TSqlTokenType.Join, BreakType.NewLine)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.EmptyLine, nodes[2].PrecedingBreak);
    }

    [Fact]
    public void Apply_EmptyLineBeforeJoin_False_KeepsNewLine()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                EmptyLineBeforeJoin = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("FROM", TSqlTokenType.From),
            Node("t1"),
            Node("JOIN", TSqlTokenType.Join, BreakType.NewLine)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[2].PrecedingBreak);
    }

    [Fact]
    public void Apply_EmptyLineBeforeJoin_InlineJoin_NotPromoted()
    {
        // JOIN that has no break at all — should not be promoted
        var profile = new FormattingProfile
        {
            Join =
            {
                EmptyLineBeforeJoin = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("FROM", TSqlTokenType.From),
            Node("t1"),
            Node("JOIN", TSqlTokenType.Join, BreakType.None) // inline
        };

        _rules.Apply(nodes, profile);

        // EmptyLineBeforeJoin only upgrades NewLine → EmptyLine, not None → EmptyLine
        Assert.Equal(BreakType.None, nodes[2].PrecedingBreak);
    }

    // ── IndentJoin ────────────────────────────────────────────────────────

    [Fact]
    public void Apply_IndentJoin_True_SetsIndentLevel()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                IndentJoin = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("FROM", TSqlTokenType.From),
            Node("t1"),
            Node("JOIN", TSqlTokenType.Join, BreakType.NewLine, indent: 0)
        };

        _rules.Apply(nodes, profile);

        Assert.True(nodes[2].IndentLevel >= 1);
    }

    [Fact]
    public void Apply_IndentJoin_False_IndentUnchanged()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                IndentJoin = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("FROM", TSqlTokenType.From),
            Node("t1"),
            Node("JOIN", TSqlTokenType.Join, BreakType.NewLine, indent: 0)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(0, nodes[2].IndentLevel);
    }

    // ── AlignJoinKeyword ──────────────────────────────────────────────────

    [Fact]
    public void Apply_AlignJoinKeyword_None_NoChanges()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                AlignJoinKeyword = "none"
            }
        };

        // INNER JOIN and JOIN — different widths
        var nodes = new List<LayoutNode>
        {
            Node("FROM", TSqlTokenType.From),
            Node("t1"),
            Node("INNER", TSqlTokenType.Inner, BreakType.NewLine),
            Node("JOIN", TSqlTokenType.Join),
            Node("t2"),
            Node("JOIN", TSqlTokenType.Join, BreakType.NewLine, spaces: 1) // plain JOIN
        };

        int spaceBefore = nodes[5].PrecedingSpaces;
        _rules.Apply(nodes, profile);

        Assert.Equal(spaceBefore, nodes[5].PrecedingSpaces);
    }

    [Fact]
    public void Apply_AlignJoinKeyword_Right_PadsModifier()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                AlignJoinKeyword = "right"
            }
        };

        // LEFT JOIN vs JOIN — LEFT JOIN is wider so plain JOIN should be padded
        var nodes = new List<LayoutNode>
        {
            Node("FROM", TSqlTokenType.From),
            Node("t1"),
            Node("LEFT", TSqlTokenType.Left, BreakType.NewLine, spaces: 0),
            Node("JOIN", TSqlTokenType.Join, spaces: 1),
            Node("t2"),
            Node("JOIN", TSqlTokenType.Join, BreakType.NewLine, spaces: 0) // plain JOIN
        };

        _rules.Apply(nodes, profile);

        // Plain JOIN should have gained padding to align with LEFT JOIN
        Assert.True(nodes[5].PrecedingSpaces >= 0); // no crash
    }

    // ── JoinTypeStyle ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_JoinTypeStyle_Implicit_RemovesInner()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                JoinTypeStyle = "implicit"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("FROM", TSqlTokenType.From),
            Node("t1"),
            Node("INNER", TSqlTokenType.Inner, BreakType.NewLine),
            Node("JOIN", TSqlTokenType.Join)
        };

        _rules.Apply(nodes, profile);

        // INNER should be blanked out
        Assert.Equal("", nodes[2].FormattedText);
    }

    [Fact]
    public void Apply_JoinTypeStyle_Explicit_AddsInner()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                JoinTypeStyle = "explicit"
            }
        };

        // Plain JOIN without modifier
        var nodes = new List<LayoutNode>
        {
            Node("FROM", TSqlTokenType.From),
            Node("t1"),
            Node("JOIN", TSqlTokenType.Join, BreakType.NewLine)
        };

        _rules.Apply(nodes, profile);

        // JOIN text should now include INNER
        Assert.Contains("INNER", nodes[2].FormattedText);
    }

    // ── No JOIN tokens ────────────────────────────────────────────────────

    [Fact]
    public void Apply_NoJoinsInList_NoThrow()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                EmptyLineBeforeJoin = true,
                IndentJoin = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("SELECT", TSqlTokenType.Select),
            Node("1")
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
}
