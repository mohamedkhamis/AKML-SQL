using System.Diagnostics.CodeAnalysis;
using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Layout;

[SuppressMessage("ReSharper", "RedundantArgumentDefaultValue")]
public class CollapseEvaluatorTests
{
    private static LayoutNode Node(string text, BreakType breakType = BreakType.None, int spaces = 1, int indent = 0,
        TSqlTokenType tokenType = TSqlTokenType.Identifier)
    {
        return new LayoutNode
        {
            FormattedText = text,
            PrecedingBreak = breakType,
            PrecedingSpaces = spaces,
            IndentLevel = indent,
            TokenType = tokenType
        };
    }

    private static LayoutNode Comma()
    {
        return new LayoutNode
        {
            FormattedText = ",",
            PrecedingBreak = BreakType.None,
            PrecedingSpaces = 0,
            TokenType = TSqlTokenType.Comma
        };
    }

    private static CollapseEvaluator Create(FormattingProfile? profile = null)
    {
        return new CollapseEvaluator(profile ?? new FormattingProfile());
    }

    // ── MeasureFormattedLength ────────────────────────────────────────────

    [Fact]
    public void MeasureFormattedLength_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, CollapseEvaluator.MeasureFormattedLength([], 0, 0));
    }

    [Fact]
    public void MeasureFormattedLength_SingleNode_ReturnsTextLength()
    {
        var nodes = new List<LayoutNode> { Node("SELECT", BreakType.None, 0) };
        Assert.Equal(6, CollapseEvaluator.MeasureFormattedLength(nodes, 0, 1));
    }

    [Fact]
    public void MeasureFormattedLength_TwoInlineNodes_IncludesSpaces()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT", BreakType.None, 0),
            Node("col1", BreakType.None, 1)
        };
        // 6 + 1 (space) + 4 = 11
        Assert.Equal(11, CollapseEvaluator.MeasureFormattedLength(nodes, 0, 2));
    }

    [Fact]
    public void MeasureFormattedLength_LineBreakCountsAsOneSpace()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT", BreakType.None, 0),
            Node("col1", BreakType.NewLine, 1)
        };
        // 6 + 1 (break→space) + 4 = 11
        Assert.Equal(11, CollapseEvaluator.MeasureFormattedLength(nodes, 0, 2));
    }

    [Fact]
    public void MeasureFormattedLength_EmptyTextNodeSkipped()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT", BreakType.None, 0),
            new() { FormattedText = "", PrecedingSpaces = 1, PrecedingBreak = BreakType.None },
            Node("col1", BreakType.None, 1)
        };
        // Empty node skipped: 6 + 1 + 4 = 11
        Assert.Equal(11, CollapseEvaluator.MeasureFormattedLength(nodes, 0, 3));
    }

    [Fact]
    public void MeasureFormattedLength_PartialRange()
    {
        var nodes = new List<LayoutNode>
        {
            Node("a", BreakType.None, 0),
            Node("b", BreakType.None, 1),
            Node("c", BreakType.None, 1)
        };
        // Range [1,3): "b" (with 1 preceding space) + "c" (with 1 preceding space) = 1+1+1+1=4
        Assert.Equal(4, CollapseEvaluator.MeasureFormattedLength(nodes, 1, 3));
    }

    // ── ShouldCollapse ────────────────────────────────────────────────────

    [Fact]
    public void ShouldCollapse_ShortContent_BelowThreshold_ReturnsTrue()
    {
        var nodes = new List<LayoutNode> { Node("SELECT", BreakType.None, 0), Node("1", BreakType.None, 1) };
        var evaluator = Create();
        Assert.True(evaluator.ShouldCollapse(nodes, 0, 2, 100));
    }

    [Fact]
    public void ShouldCollapse_LongContent_AboveThreshold_ReturnsFalse()
    {
        var nodes = new List<LayoutNode>
        {
            Node(new string('x', 50), BreakType.None, 0),
            Node(new string('y', 50), BreakType.None, 1)
        };
        var evaluator = Create();
        Assert.False(evaluator.ShouldCollapse(nodes, 0, 2, 10));
    }

    [Fact]
    public void ShouldCollapse_InvalidRange_StartGteEnd_ReturnsFalse()
    {
        var nodes = new List<LayoutNode> { Node("SELECT", BreakType.None, 0) };
        var evaluator = Create();
        Assert.False(evaluator.ShouldCollapse(nodes, 1, 0, 100));
    }

    [Fact]
    public void ShouldCollapse_NegativeStart_ReturnsFalse()
    {
        var nodes = new List<LayoutNode> { Node("SELECT", BreakType.None, 0) };
        var evaluator = Create();
        Assert.False(evaluator.ShouldCollapse(nodes, -1, 1, 100));
    }

    [Fact]
    public void ShouldCollapse_EndBeyondList_ReturnsFalse()
    {
        var nodes = new List<LayoutNode> { Node("SELECT", BreakType.None, 0) };
        var evaluator = Create();
        Assert.False(evaluator.ShouldCollapse(nodes, 0, 5, 100));
    }

    // ── ShouldCollapseSelectList ──────────────────────────────────────────

    [Fact]
    public void ShouldCollapseSelectList_CollapseDisabled_ReturnsFalse()
    {
        var profile = new FormattingProfile
        {
            List =
            {
                CollapseShortLists = false
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("col1", BreakType.None, 0) };
        Assert.False(evaluator.ShouldCollapseSelectList(nodes, 0, 1));
    }

    [Fact]
    public void ShouldCollapseSelectList_CollapseEnabled_ShortList_ReturnsTrue()
    {
        var profile = new FormattingProfile
        {
            List =
            {
                CollapseShortLists = true,
                CollapseThreshold = 100
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("col1", BreakType.None, 0), Comma(), Node("col2", BreakType.None, 1) };
        Assert.True(evaluator.ShouldCollapseSelectList(nodes, 0, 3));
    }

    // ── ShouldCollapseParenthesized ───────────────────────────────────────

    [Fact]
    public void ShouldCollapseParenthesized_CollapseDisabled_ReturnsFalse()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                CollapseShort = false
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("1", BreakType.None, 0) };
        Assert.False(evaluator.ShouldCollapseParenthesized(nodes, 0, 1));
    }

    [Fact]
    public void ShouldCollapseParenthesized_ContainsSelect_ReturnsFalse()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                CollapseShort = true,
                CollapseThreshold = 1000
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode>
        {
            Node("SELECT", BreakType.None, 0, tokenType: TSqlTokenType.Select),
            Node("1", BreakType.None, 1)
        };
        Assert.False(evaluator.ShouldCollapseParenthesized(nodes, 0, 2));
    }

    [Fact]
    public void ShouldCollapseParenthesized_NoSelect_Short_ReturnsTrue()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                CollapseShort = true,
                CollapseThreshold = 100
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("a", BreakType.None, 0), Node("b", BreakType.None, 1) };
        Assert.True(evaluator.ShouldCollapseParenthesized(nodes, 0, 2));
    }

    // ── ShouldCollapseDmlStatement ────────────────────────────────────────

    [Fact]
    public void ShouldCollapseDmlStatement_CollapseDisabled_ReturnsFalse()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                CollapseShortStatements = false
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("SELECT 1", BreakType.None, 0) };
        Assert.False(evaluator.ShouldCollapseDmlStatement(nodes, 0, 1));
    }

    [Fact]
    public void ShouldCollapseDmlStatement_SubquerySelect_ReturnsFalse()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                CollapseShortStatements = true,
                CollapseThreshold = 1000
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode>
        {
            Node("(", BreakType.None, 0, tokenType: TSqlTokenType.LeftParenthesis),
            Node("SELECT", BreakType.None, 0, tokenType: TSqlTokenType.Select),
            Node("1", BreakType.None, 1),
            Node(")", BreakType.None, 0, tokenType: TSqlTokenType.RightParenthesis)
        };
        Assert.False(evaluator.ShouldCollapseDmlStatement(nodes, 0, 4));
    }

    // ── ShouldCollapseCase ────────────────────────────────────────────────

    [Fact]
    public void ShouldCollapseCase_CollapseDisabled_ReturnsFalse()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                CollapseShortCase = false
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("CASE WHEN 1=1 THEN 'a' END", BreakType.None, 0) };
        Assert.False(evaluator.ShouldCollapseCase(nodes, 0, 1));
    }

    [Fact]
    public void ShouldCollapseCase_ShortCase_ReturnsTrue()
    {
        var profile = new FormattingProfile
        {
            Case =
            {
                CollapseShortCase = true,
                CollapseThreshold = 100
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("a", BreakType.None, 0) };
        Assert.True(evaluator.ShouldCollapseCase(nodes, 0, 1));
    }

    // ── ShouldCollapseIfElse ──────────────────────────────────────────────

    [Fact]
    public void ShouldCollapseIfElse_CollapseDisabled_ReturnsFalse()
    {
        var profile = new FormattingProfile
        {
            ControlFlow =
            {
                CollapseShortIfElse = false
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("a", BreakType.None, 0) };
        Assert.False(evaluator.ShouldCollapseIfElse(nodes, 0, 1));
    }

    [Fact]
    public void ShouldCollapseIfElse_ContainsBegin_ReturnsFalse()
    {
        var profile = new FormattingProfile
        {
            ControlFlow =
            {
                CollapseShortIfElse = true,
                CollapseThreshold = 1000
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode>
        {
            Node("BEGIN", BreakType.None, 0, tokenType: TSqlTokenType.Begin),
            Node("END", BreakType.None, 0, tokenType: TSqlTokenType.End)
        };
        Assert.False(evaluator.ShouldCollapseIfElse(nodes, 0, 2));
    }

    // ── ShouldCollapseDdl ─────────────────────────────────────────────────

    [Fact]
    public void ShouldCollapseDdl_CollapseDisabled_ReturnsFalse()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                CollapseShortDdl = false
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("a", BreakType.None, 0) };
        Assert.False(evaluator.ShouldCollapseDdl(nodes, 0, 1));
    }

    [Fact]
    public void ShouldCollapseDdl_ShortDdl_ReturnsTrue()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                CollapseShortDdl = true,
                CollapseThreshold = 100
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("a", BreakType.None, 0) };
        Assert.True(evaluator.ShouldCollapseDdl(nodes, 0, 1));
    }

    // ── ShouldCollapseSubquery ────────────────────────────────────────────

    [Fact]
    public void ShouldCollapseSubquery_CollapseDisabled_ReturnsFalse()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                CollapseShortSubqueries = false
            }
        };
        var evaluator = Create(profile);
        var nodes = new List<LayoutNode> { Node("a", BreakType.None, 0) };
        Assert.False(evaluator.ShouldCollapseSubquery(nodes, 0, 1));
    }

    // ── Collapse (static) ─────────────────────────────────────────────────

    [Fact]
    public void Collapse_RemovesLineBreaks()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT", BreakType.None, 0),
            Node("col1", BreakType.NewLine, 1, indent: 1),
            Comma(),
            Node("col2", BreakType.NewLine, 1, indent: 1)
        };

        CollapseEvaluator.Collapse(nodes, 0, 4);

        Assert.Equal(BreakType.None, nodes[1].PrecedingBreak);
        Assert.Equal(BreakType.None, nodes[3].PrecedingBreak);
        Assert.Equal(0, nodes[1].IndentLevel);
        Assert.Equal(0, nodes[3].IndentLevel);
    }

    [Fact]
    public void Collapse_CommaGetsZeroSpaces()
    {
        var nodes = new List<LayoutNode>
        {
            Node("col1", BreakType.None, 0),
            new() { FormattedText = ",", PrecedingBreak = BreakType.NewLine, PrecedingSpaces = 0, TokenType = TSqlTokenType.Comma }
        };

        CollapseEvaluator.Collapse(nodes, 0, 2);

        Assert.Equal(0, nodes[1].PrecedingSpaces);
    }

    [Fact]
    public void Collapse_NonCommaGetsOneSpace()
    {
        var nodes = new List<LayoutNode>
        {
            Node("a", BreakType.None, 0),
            Node("b", BreakType.NewLine, 0)
        };

        CollapseEvaluator.Collapse(nodes, 0, 2);

        Assert.Equal(1, nodes[1].PrecedingSpaces);
    }

    // ── CollapseIfShort ───────────────────────────────────────────────────

    [Fact]
    public void CollapseIfShort_ShortContent_CollapsesAndReturnsTrue()
    {
        var nodes = new List<LayoutNode>
        {
            Node("a", BreakType.None, 0),
            Node("b", BreakType.NewLine, 0)
        };
        var evaluator = Create();
        var result = evaluator.CollapseIfShort(nodes, 0, 2, 100);
        Assert.True(result);
        Assert.Equal(BreakType.None, nodes[1].PrecedingBreak);
    }

    [Fact]
    public void CollapseIfShort_LongContent_DoesNotCollapseAndReturnsFalse()
    {
        var nodes = new List<LayoutNode>
        {
            Node(new string('x', 50), BreakType.None, 0),
            Node(new string('y', 50), BreakType.NewLine, 0)
        };
        var evaluator = Create();
        var result = evaluator.CollapseIfShort(nodes, 0, 2, 10);
        Assert.False(result);
        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
    }

    // ── CountLines ────────────────────────────────────────────────────────

    [Fact]
    public void CountLines_EmptyRange_ReturnsZero()
    {
        Assert.Equal(0, CollapseEvaluator.CountLines([], 0, 0));
    }

    [Fact]
    public void CountLines_SingleNode_ReturnsOne()
    {
        var nodes = new List<LayoutNode> { Node("a", BreakType.None, 0) };
        Assert.Equal(1, CollapseEvaluator.CountLines(nodes, 0, 1));
    }

    [Fact]
    public void CountLines_MultipleNodes_NoBreaks_ReturnsOne()
    {
        var nodes = new List<LayoutNode>
        {
            Node("a", BreakType.None, 0),
            Node("b", BreakType.None, 1),
            Node("c", BreakType.None, 1)
        };
        Assert.Equal(1, CollapseEvaluator.CountLines(nodes, 0, 3));
    }

    [Fact]
    public void CountLines_WithBreaks_CountsCorrectly()
    {
        var nodes = new List<LayoutNode>
        {
            Node("a", BreakType.None, 0),
            Node("b", BreakType.NewLine, 0),
            Node("c", BreakType.NewLine, 0)
        };
        Assert.Equal(3, CollapseEvaluator.CountLines(nodes, 0, 3));
    }

    // ── MaxLineWidth ──────────────────────────────────────────────────────

    [Fact]
    public void MaxLineWidth_SingleLine_ReturnsTotalWidth()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT", BreakType.None, 0),
            Node("col1", BreakType.None, 1)
        };
        // "SELECT" + 1 space + "col1" = 6+1+4=11
        Assert.Equal(11, CollapseEvaluator.MaxLineWidth(nodes, 0, 2, tabSize: 4));
    }

    [Fact]
    public void MaxLineWidth_MultipleLines_ReturnsMaximum()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT", BreakType.None, 0),
            Node("col1_with_long_name", BreakType.NewLine, 0, indent: 1),
            Node("b", BreakType.NewLine, 0, indent: 0)
        };
        // Line 1: "SELECT" = 6
        // Line 2: indent 1*4=4 + "col1_with_long_name" = 4+19=23
        // Line 3: "b" = 1
        Assert.Equal(23, CollapseEvaluator.MaxLineWidth(nodes, 0, 3, tabSize: 4));
    }

    // ── MeasureParenthesizedLength ────────────────────────────────────────

    [Fact]
    public void MeasureParenthesizedLength_ExcludesParens()
    {
        var nodes = new List<LayoutNode>
        {
            Node("(", BreakType.None, 0, tokenType: TSqlTokenType.LeftParenthesis),
            Node("a", BreakType.None, 0),
            Node("b", BreakType.None, 1),
            Node(")", BreakType.None, 0, tokenType: TSqlTokenType.RightParenthesis)
        };
        // Between index 0 and 3 (exclusive of parens): nodes 1,2 → "a" + 1 + "b" = 3
        Assert.Equal(3, CollapseEvaluator.MeasureParenthesizedLength(nodes, 0, 3));
    }
}
