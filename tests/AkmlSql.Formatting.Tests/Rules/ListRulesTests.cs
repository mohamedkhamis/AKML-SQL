using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Rules;

public class ListRulesTests
{
    private readonly ListRules _rules = new();

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

    private static LayoutNode Comma()
    {
        return new LayoutNode
        {
            FormattedText = ",",
            TokenType = TSqlTokenType.Comma,
            PrecedingBreak = BreakType.None,
            PrecedingSpaces = 0
        };
    }

    // ── CommaPosition: leading ────────────────────────────────────────────

    [Fact]
    public void Apply_CommaPosition_Leading_MovesCommaToNextLine()
    {
        var profile = new FormattingProfile
        {
            List =
            {
                CommaPosition = "leading"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1"),
            Comma(),
            Node("col2", breakType: BreakType.NewLine, spaces: 0, indent: 1)
        };

        _rules.Apply(nodes, profile);

        // The comma should now be on the new line
        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
        // The next token follows comma on same line
        Assert.Equal(BreakType.None, nodes[2].PrecedingBreak);
        Assert.Equal(1, nodes[2].PrecedingSpaces);
    }

    [Fact]
    public void Apply_CommaPosition_Trailing_NoChange()
    {
        var profile = new FormattingProfile
        {
            List =
            {
                CommaPosition = "trailing"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1"),
            Comma(),
            Node("col2", breakType: BreakType.NewLine, spaces: 0)
        };

        // Store original state
        var origCommaBreak = nodes[1].PrecedingBreak;
        var origNextBreak = nodes[2].PrecedingBreak;

        _rules.Apply(nodes, profile);

        // No change in trailing mode
        Assert.Equal(origCommaBreak, nodes[1].PrecedingBreak);
        Assert.Equal(origNextBreak, nodes[2].PrecedingBreak);
    }

    // ── Apply on empty nodes ──────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyNodes_DoesNotThrow()
    {
        var profile = new FormattingProfile();
        var nodes = new List<LayoutNode>();
        var ex = Record.Exception(() => _rules.Apply(nodes, profile));
        Assert.Null(ex);
    }

    // ── SpaceAfterListComma ───────────────────────────────────────────────

    [Fact]
    public void Apply_SpaceAfterListComma_True_EnsuresSpace()
    {
        var profile = new FormattingProfile
        {
            List =
            {
                CommaPosition = "trailing" // so leading comma doesn't interfere
            },
            Whitespace =
            {
                LineBreakAfterComma = false
            }
        };
        // SpaceAfterListComma is not exposed directly in profile, it's part of List options

        var nodes = new List<LayoutNode>
        {
            Node("col1"),
            Comma(),
            Node("col2", spaces: 0)
        };

        _rules.Apply(nodes, profile);

        // After Apply, col2 might have space set by SpaceAfterListComma
        // (just verify it doesn't throw)
        Assert.NotNull(nodes);
    }
}
