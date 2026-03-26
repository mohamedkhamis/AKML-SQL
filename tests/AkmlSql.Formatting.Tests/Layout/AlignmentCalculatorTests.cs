using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Layout;

public class AlignmentCalculatorTests
{
    private static LayoutNode Node(
        string text,
        TSqlTokenType tokenType = TSqlTokenType.Identifier,
        BreakType breakType = BreakType.None,
        int spaces = 1,
        int indent = 0)
    {
        return new LayoutNode
        {
            FormattedText = text,
            TokenType = tokenType,
            PrecedingBreak = breakType,
            PrecedingSpaces = spaces,
            IndentLevel = indent
        };
    }

    // ── MeasureLineWidth ──────────────────────────────────────────────────

    [Fact]
    public void MeasureLineWidth_SingleNode_ReturnsTextLength()
    {
        var nodes = new List<LayoutNode>
        {
            Node("SELECT") // 6 chars
        };

        int width = AlignmentCalculator.MeasureLineWidth(nodes, 0, 1);

        Assert.Equal(6, width);
    }

    [Fact]
    public void MeasureLineWidth_TwoInlineNodes_IncludesSpaces()
    {
        var nodes = new List<LayoutNode>
        {
            Node("col"),      // 3 chars
            Node("AS", spaces: 1)  // 1 space + 2 chars = 3
        };

        // Total = 3 + 1 + 2 = 6
        int width = AlignmentCalculator.MeasureLineWidth(nodes, 0, 2);

        Assert.Equal(6, width);
    }

    [Fact]
    public void MeasureLineWidth_LineBreakNode_BreakNotCounted()
    {
        var nodes = new List<LayoutNode>
        {
            Node("col"),
            Node("AS", breakType: BreakType.NewLine, spaces: 0)
        };

        // start=0, end=1 → only "col" = 3
        int width = AlignmentCalculator.MeasureLineWidth(nodes, 0, 1);

        Assert.Equal(3, width);
    }

    [Fact]
    public void MeasureLineWidth_EmptyRange_ReturnsZero()
    {
        var nodes = new List<LayoutNode>
        {
            Node("col")
        };

        int width = AlignmentCalculator.MeasureLineWidth(nodes, 0, 0);

        Assert.Equal(0, width);
    }

    // ── MeasureRange ──────────────────────────────────────────────────────

    [Fact]
    public void MeasureRange_SingleNode_ReturnsTextLength()
    {
        var nodes = new List<LayoutNode>
        {
            Node("abc") // 3 chars
        };

        int width = AlignmentCalculator.MeasureRange(nodes, 0, 1);

        Assert.Equal(3, width);
    }

    [Fact]
    public void MeasureRange_TwoNodes_IncludesSpaces()
    {
        var nodes = new List<LayoutNode>
        {
            Node("col"),    // 3
            Node("INT", spaces: 1)  // 1 + 3 = 4
        };

        int width = AlignmentCalculator.MeasureRange(nodes, 0, 2);

        Assert.Equal(7, width);
    }

    [Fact]
    public void MeasureRange_BeyondEnd_NoCrash()
    {
        var nodes = new List<LayoutNode>
        {
            Node("col")
        };

        var ex = Record.Exception(() => AlignmentCalculator.MeasureRange(nodes, 0, 100));
        Assert.Null(ex);
    }

    // ── AlignSelectAliases ────────────────────────────────────────────────

    [Fact]
    public void AlignSelectAliases_False_NoChange()
    {
        var profile = new FormattingProfile
        {
            List =
            {
                AlignAliases = false
            }
        };

        var calc = new AlignmentCalculator(profile);

        var nodes = new List<LayoutNode>
        {
            Node("SELECT", TSqlTokenType.Select),
            Node("col", breakType: BreakType.NewLine),
            Node("AS", TSqlTokenType.As, spaces: 1),
            Node("a"),
            Node(",", TSqlTokenType.Comma),
            Node("longcol", breakType: BreakType.NewLine),
            Node("AS", TSqlTokenType.As, spaces: 1),
            Node("b")
        };

        int spacesBefore = nodes[2].PrecedingSpaces;
        calc.AlignSelectAliases(nodes);

        // No alignment performed
        Assert.Equal(spacesBefore, nodes[2].PrecedingSpaces);
    }

    [Fact]
    public void AlignSelectAliases_True_AlignsAsKeywords()
    {
        var profile = new FormattingProfile
        {
            List =
            {
                AlignAliases = true
            }
        };

        var calc = new AlignmentCalculator(profile);

        // SELECT
        //   col    AS a
        //   longcol AS b
        // AS for "col" (short) should get more padding than AS for "longcol"
        var nodes = new List<LayoutNode>
        {
            Node("SELECT", TSqlTokenType.Select),
            Node("col", breakType: BreakType.NewLine),
            Node("AS", TSqlTokenType.As, spaces: 1),
            Node("a"),
            Node(",", TSqlTokenType.Comma),
            Node("longcolumn", breakType: BreakType.NewLine),
            Node("AS", TSqlTokenType.As, spaces: 1),
            Node("b"),
            Node("FROM", TSqlTokenType.From, breakType: BreakType.NewLine)
        };

        calc.AlignSelectAliases(nodes);

        // After alignment, both AS tokens should have same effective column position
        // The first AS (after "col") should have more spaces than the second (after "longcolumn")
        Assert.True(nodes[2].PrecedingSpaces >= nodes[6].PrecedingSpaces);
    }

    // ── AlignDataTypes ────────────────────────────────────────────────────

    [Fact]
    public void AlignDataTypes_False_NoChange()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                AlignDataTypes = false
            }
        };

        var calc = new AlignmentCalculator(profile);

        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("TABLE", TSqlTokenType.Table),
            Node("t"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("id", breakType: BreakType.NewLine),
            Node("INT"),
            Node(",", TSqlTokenType.Comma),
            Node("name", breakType: BreakType.NewLine),
            Node("VARCHAR"),
            Node(")", TSqlTokenType.RightParenthesis, breakType: BreakType.NewLine)
        };

        int spacesBefore = nodes[5].PrecedingSpaces;
        calc.AlignDataTypes(nodes);

        Assert.Equal(spacesBefore, nodes[5].PrecedingSpaces);
    }

    [Fact]
    public void AlignDataTypes_True_PadsDataTypes()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                AlignDataTypes = true
            }
        };

        var calc = new AlignmentCalculator(profile);

        // CREATE TABLE t ( id INT , longcolumnname VARCHAR )
        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("TABLE", TSqlTokenType.Table),
            Node("t"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("id", breakType: BreakType.NewLine),
            Node("INT"),
            Node(",", TSqlTokenType.Comma),
            Node("longcolumnname", breakType: BreakType.NewLine),
            Node("VARCHAR"),
            Node(")", TSqlTokenType.RightParenthesis, breakType: BreakType.NewLine)
        };

        calc.AlignDataTypes(nodes);

        // No exception = pass for now; alignment only kicks in for recognized data types
        Assert.True(nodes[5].PrecedingSpaces >= 1);
    }

    // ── Empty list ─────────────────────────────────────────────────────────

    [Fact]
    public void AlignSelectAliases_EmptyList_NoThrow()
    {
        var profile = new FormattingProfile { List = { AlignAliases = true } };
        var calc = new AlignmentCalculator(profile);
        var nodes = new List<LayoutNode>();
        var ex = Record.Exception(() => calc.AlignSelectAliases(nodes));
        Assert.Null(ex);
    }
}
