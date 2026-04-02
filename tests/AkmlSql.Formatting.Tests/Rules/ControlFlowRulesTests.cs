using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Rules;

public class ControlFlowRulesTests
{
    private readonly ControlFlowRules _rules = new();

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

    // ── ElseOnNewLine ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_ElseOnNewLine_True_AddsBreakBeforeElse()
    {
        var profile = new FormattingProfile
        {
            ControlFlow =
            {
                ElseOnNewLine = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("IF", TSqlTokenType.If),
            Node("("),
            Node("1=1"),
            Node(")"),
            Node("SELECT", TSqlTokenType.Select),
            Node("1"),
            Node("ELSE", TSqlTokenType.Else) // inline — should be moved to new line
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[6].PrecedingBreak);
    }

    [Fact]
    public void Apply_ElseOnNewLine_False_NoChange()
    {
        var profile = new FormattingProfile
        {
            ControlFlow =
            {
                ElseOnNewLine = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("IF", TSqlTokenType.If),
            Node("(1=1)"),
            Node("SELECT 1"),
            Node("ELSE", TSqlTokenType.Else) // inline
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.None, nodes[3].PrecedingBreak);
    }

    // ── ElseAlignWithIf ───────────────────────────────────────────────────

    [Fact]
    public void Apply_ElseAlignWithIf_True_AlignsIndent()
    {
        var profile = new FormattingProfile
        {
            ControlFlow =
            {
                ElseOnNewLine = true,
                ElseAlignWithIf = true
            }
        };

        // IF at indent 0, ELSE should also be indent 0
        var nodes = new List<LayoutNode>
        {
            Node("IF", TSqlTokenType.If, indent: 0),
            Node("SELECT 1"),
            Node("ELSE", TSqlTokenType.Else, indent: 2) // misaligned
        };

        _rules.Apply(nodes, profile);

        // ELSE should be aligned with IF (indent 0)
        Assert.Equal(0, nodes[2].IndentLevel);
    }

    // ── Noformat region ───────────────────────────────────────────────────

    [Fact]
    public void Apply_NoformatRegion_ElseNotChanged()
    {
        var profile = new FormattingProfile
        {
            ControlFlow =
            {
                ElseOnNewLine = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("IF", TSqlTokenType.If),
            Node("SELECT 1"),
            Node("ELSE", TSqlTokenType.Else, inNoformat: true) // in noformat
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.None, nodes[2].PrecedingBreak);
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

    // ── No control flow tokens ────────────────────────────────────────────

    [Fact]
    public void Apply_NoControlFlowTokens_NoThrow()
    {
        var profile = new FormattingProfile
        {
            ControlFlow =
            {
                ElseOnNewLine = true,
                CollapseShortIfElse = true
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

    // ── CollapseShortIfElse ───────────────────────────────────────────────

    [Fact]
    public void Apply_CollapseShortIfElse_True_ShortIfCollapsed()
    {
        var profile = new FormattingProfile
        {
            ControlFlow =
            {
                CollapseShortIfElse = true,
                CollapseThreshold = 200
            }
        };

        // IF (1=1) PRINT 'yes' — PRINT is not a statement-start keyword so collapse works
        var nodes = new List<LayoutNode>
        {
            Node("IF", TSqlTokenType.If),
            Node("(1=1)"),
            Node("PRINT", TSqlTokenType.Identifier, BreakType.NewLine, indent: 1),  // body on new line
            Node("'yes'")
        };

        _rules.Apply(nodes, profile);

        // After collapse, PRINT should have no break
        Assert.Equal(BreakType.None, nodes[2].PrecedingBreak);
    }

    // ── CASE rules ────────────────────────────────────────────────────────

    [Fact]
    public void Apply_CaseWhenOnNewLine_True_AddsBreakBeforeWhen()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                WhenOnNewLine = true,
                CollapseShortCase = false   // prevent collapse from undoing the break
            }
        };

        // Full CASE block — ApplyCaseRules only fires when END is encountered
        var nodes = new List<LayoutNode>
        {
            Node("CASE", TSqlTokenType.Case),
            Node("WHEN", TSqlTokenType.When),    // inline — should get NewLine
            Node("1"),
            Node("THEN", TSqlTokenType.Then),
            Node("'a'"),
            Node("END", TSqlTokenType.End)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
    }

    [Fact]
    public void Apply_CaseEndOnNewLine_True_AddsBreakBeforeEnd()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                EndOnNewLine = true,
                CollapseShortCase = false   // prevent collapse from undoing the break
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CASE", TSqlTokenType.Case),
            Node("WHEN", TSqlTokenType.When, BreakType.NewLine),
            Node("1"),
            Node("THEN", TSqlTokenType.Then),
            Node("a"),
            Node("END", TSqlTokenType.End) // inline
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[5].PrecedingBreak);
    }
}
