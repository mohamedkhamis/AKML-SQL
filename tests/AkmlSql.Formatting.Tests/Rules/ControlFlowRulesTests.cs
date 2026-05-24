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

    // ── T082 — FirstWhenOnNewLine ────────────────────────────────────────

    [Fact]
    public void T082_FirstWhenOnNewLine_Always_BreaksFirstWhenEvenWhenWhenOnNewLineFalse()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                WhenOnNewLine = false,
                FirstWhenOnNewLine = "always",
                CollapseShortCase = false,
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CASE", TSqlTokenType.Case),
            Node("x"),
            Node("WHEN", TSqlTokenType.When),    // inline — should be broken
            Node("1"),
            Node("THEN", TSqlTokenType.Then),
            Node("a"),
            Node("END", TSqlTokenType.End)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[2].PrecedingBreak);
    }

    [Fact]
    public void T082_FirstWhenOnNewLine_Never_KeepsFirstWhenInlineEvenWhenWhenOnNewLineTrue()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                WhenOnNewLine = true,
                FirstWhenOnNewLine = "never",
                CollapseShortCase = false,
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CASE", TSqlTokenType.Case),
            Node("WHEN", TSqlTokenType.When, BreakType.NewLine),    // pre-broken — should be re-inlined
            Node("1"),
            Node("THEN", TSqlTokenType.Then),
            Node("a"),
            Node("END", TSqlTokenType.End)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.None, nodes[1].PrecedingBreak);
    }

    // ── T082 — ExpressionOnNewLine ───────────────────────────────────────

    [Fact]
    public void T082_ExpressionOnNewLine_True_PlacesSimpleCaseExpressionOnNextLine()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                ExpressionOnNewLine = true,
                CollapseShortCase = false,
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CASE", TSqlTokenType.Case),
            Node("x"),                              // simple-case expression — should be broken
            Node("WHEN", TSqlTokenType.When, BreakType.NewLine),
            Node("1"),
            Node("THEN", TSqlTokenType.Then),
            Node("a"),
            Node("END", TSqlTokenType.End)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
    }

    // ── T082 — WhenAlignment ─────────────────────────────────────────────

    [Fact]
    public void T082_WhenAlignment_IndentedFromCase_PutsWhenAtIndentPlusOne()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                WhenOnNewLine = true,
                WhenAlignment = "indentedFromCase",
                CollapseShortCase = false,
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CASE", TSqlTokenType.Case, indent: 2),
            Node("WHEN", TSqlTokenType.When),
            Node("1"),
            Node("THEN", TSqlTokenType.Then),
            Node("a"),
            Node("END", TSqlTokenType.End)
        };

        _rules.Apply(nodes, profile);

        // CASE is at indent 2, indentedFromCase => WHEN at indent 3
        Assert.Equal(3, nodes[1].IndentLevel);
    }

    [Fact]
    public void T082_WhenAlignment_ToCase_KeepsWhenAtCaseIndent()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                WhenOnNewLine = true,
                WhenAlignment = "toCase",
                IndentWhen = false,    // toCase overrides legacy IndentWhen
                CollapseShortCase = false,
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CASE", TSqlTokenType.Case, indent: 2),
            Node("WHEN", TSqlTokenType.When),
            Node("1"),
            Node("THEN", TSqlTokenType.Then),
            Node("a"),
            Node("END", TSqlTokenType.End)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(2, nodes[1].IndentLevel);
    }

    // ── T083 — Operators.BetweenOnNewLine ────────────────────────────────

    [Fact]
    public void T083_OperatorsBetweenOnNewLine_True_BreaksBeforeBetween()
    {
        var profile = new FormattingProfile
        {
            Operators = { BetweenOnNewLine = true }
        };

        var nodes = new List<LayoutNode>
        {
            Node("x"),
            Node("BETWEEN", TSqlTokenType.Between),    // inline — should be broken
            Node("1"),
            Node("AND", TSqlTokenType.And),
            Node("2")
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
    }

    [Fact]
    public void T083_OperatorsBetweenOnNewLine_False_LeavesBetweenInline()
    {
        var profile = new FormattingProfile
        {
            Operators = { BetweenOnNewLine = false }
        };

        var nodes = new List<LayoutNode>
        {
            Node("x"),
            Node("BETWEEN", TSqlTokenType.Between),
            Node("1"),
            Node("AND", TSqlTokenType.And),
            Node("2")
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.None, nodes[1].PrecedingBreak);
    }

    // ── T083 — Operators.Alignment ───────────────────────────────────────

    [Fact]
    public void T083_OperatorsAlignment_IndentedFromStatement_BumpsAndOrIndent()
    {
        var profile = new FormattingProfile
        {
            Operators = { Alignment = "indentedFromStatement" }
        };

        var nodes = new List<LayoutNode>
        {
            Node("x"),
            Node("AND", TSqlTokenType.And, BreakType.NewLine, indent: 2),    // already on new line — should bump
            Node("y"),
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(3, nodes[1].IndentLevel);
    }

    [Fact]
    public void T083_OperatorsAlignment_InlineWithStatement_LeavesIndentUnchanged()
    {
        var profile = new FormattingProfile
        {
            Operators = { Alignment = "inlineWithStatement" }
        };

        var nodes = new List<LayoutNode>
        {
            Node("x"),
            Node("AND", TSqlTokenType.And, BreakType.NewLine, indent: 2),
            Node("y"),
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(2, nodes[1].IndentLevel);
    }

    // ── Phase B closure — Operators.AndBetweenOnNewLine ──────────────────

    [Fact]
    public void PhaseB_AndBetweenOnNewLine_True_BreaksBeforeAndInBetween()
    {
        var profile = new FormattingProfile
        {
            Operators = { AndBetweenOnNewLine = true },
            // Disable the BetweenOnOneLine guard so the AND break can actually happen
            Expression = { BetweenOnOneLine = false },
        };

        var nodes = new List<LayoutNode>
        {
            Node("x"),
            Node("BETWEEN", TSqlTokenType.Between),
            Node("1"),
            Node("AND", TSqlTokenType.And),    // inline — should be broken
            Node("2")
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[3].PrecedingBreak);
    }

    [Fact]
    public void PhaseB_AndBetweenOnNewLine_True_LeavesAndAloneWhenBetweenOnOneLineWins()
    {
        var profile = new FormattingProfile
        {
            Operators = { AndBetweenOnNewLine = true },
            Expression = { BetweenOnOneLine = true },    // overrides — pulls AND back inline
        };

        var nodes = new List<LayoutNode>
        {
            Node("x"),
            Node("BETWEEN", TSqlTokenType.Between),
            Node("1"),
            Node("AND", TSqlTokenType.And),
            Node("2")
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.None, nodes[3].PrecedingBreak);
    }

    // ── Phase B closure — Case.EndAlignment ──────────────────────────────

    [Fact]
    public void PhaseB_CaseEndAlignment_Indented_PutsEndAtCasePlusOne()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                EndOnNewLine = true,
                EndAlignment = "indented",
                CollapseShortCase = false,
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CASE", TSqlTokenType.Case, indent: 3),
            Node("WHEN", TSqlTokenType.When, BreakType.NewLine),
            Node("1"),
            Node("THEN", TSqlTokenType.Then),
            Node("a"),
            Node("END", TSqlTokenType.End)
        };

        _rules.Apply(nodes, profile);

        // CASE is at indent 3, EndAlignment=indented => END at indent 4
        Assert.Equal(4, nodes[5].IndentLevel);
    }

    [Fact]
    public void PhaseB_CaseEndAlignment_ToCase_KeepsEndAtCaseIndent()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                EndOnNewLine = true,
                EndAlignment = "toCase",      // default
                CollapseShortCase = false,
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CASE", TSqlTokenType.Case, indent: 3),
            Node("WHEN", TSqlTokenType.When, BreakType.NewLine),
            Node("1"),
            Node("THEN", TSqlTokenType.Then),
            Node("a"),
            Node("END", TSqlTokenType.End)
        };

        _rules.Apply(nodes, profile);

        // toCase => END stays at the CASE indent level (3)
        Assert.Equal(3, nodes[5].IndentLevel);
    }

    // ── Phase B closure — Cte.AsOnNewLine ────────────────────────────────

    [Fact]
    public void PhaseB_CteAsOnNewLine_True_BreaksBeforeAs()
    {
        var profile = new FormattingProfile
        {
            Cte = { AsOnNewLine = true },
        };

        var nodes = new List<LayoutNode>
        {
            Node("WITH", TSqlTokenType.With),
            Node("cte_name", TSqlTokenType.Identifier),
            Node("AS", TSqlTokenType.As),    // inline — should be broken
            Node("("),
            Node("SELECT", TSqlTokenType.Select),
            Node("1"),
            Node(")")
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[2].PrecedingBreak);
    }

    // ── Phase B closure — FunctionCalls.PlaceParametersOnNewLine ─────────

    [Fact]
    public void PhaseB_FunctionCallsAlways_BreaksAllParameters()
    {
        var profile = new FormattingProfile
        {
            FunctionCalls =
            {
                PlaceParametersOnNewLine = "always",
                IndentParameters = true,
            },
        };

        var nodes = new List<LayoutNode>
        {
            Node("ISNULL", TSqlTokenType.Identifier, indent: 0),
            Node("(", TSqlTokenType.LeftParenthesis, spaces: 0),    // touching identifier
            Node("a"),
            Node(",", TSqlTokenType.Comma, spaces: 0),
            Node("b"),
            Node(")", TSqlTokenType.RightParenthesis, spaces: 0),
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[2].PrecedingBreak);    // first param
        Assert.Equal(BreakType.NewLine, nodes[4].PrecedingBreak);    // post-comma param
        Assert.Equal(BreakType.NewLine, nodes[5].PrecedingBreak);    // close paren
        Assert.Equal(1, nodes[2].IndentLevel);                       // indented one from parent (0)
    }

    [Fact]
    public void PhaseB_FunctionCallsNever_CollapsesInternalBreaks()
    {
        var profile = new FormattingProfile
        {
            FunctionCalls = { PlaceParametersOnNewLine = "never" },
        };

        var nodes = new List<LayoutNode>
        {
            Node("ISNULL", TSqlTokenType.Identifier),
            Node("(", TSqlTokenType.LeftParenthesis, spaces: 0),
            Node("a", breakType: BreakType.NewLine),    // pre-broken — should be collapsed
            Node(",", TSqlTokenType.Comma, spaces: 0),
            Node("b", breakType: BreakType.NewLine),
            Node(")", TSqlTokenType.RightParenthesis),
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.None, nodes[2].PrecedingBreak);
        Assert.Equal(BreakType.None, nodes[4].PrecedingBreak);
    }
}
