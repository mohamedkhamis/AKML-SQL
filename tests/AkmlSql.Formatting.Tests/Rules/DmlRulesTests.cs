using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Rules;

public class DmlRulesTests
{
    private readonly DmlRules _rules = new();

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

    // ── AndOrNewLine "after" ───────────────────────────────────────────────

    [Fact]
    public void Apply_AndOrNewLine_After_MoveBreakToAfterAnd()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                AndOrNewLine = "after"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1 = 1"),
            Node("AND", TSqlTokenType.And, BreakType.NewLine, indent: 1),
            Node("col2 = 2", spaces: 1)
        };

        _rules.Apply(nodes, profile);

        // AND should have break removed
        Assert.Equal(BreakType.None, nodes[1].PrecedingBreak);
        Assert.Equal(1, nodes[1].PrecedingSpaces);
        // Token after AND should get the break
        Assert.Equal(BreakType.NewLine, nodes[2].PrecedingBreak);
    }

    [Fact]
    public void Apply_AndOrNewLine_After_OrToken_MoveBreakToAfterOr()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                AndOrNewLine = "after"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("a = 1"),
            Node("OR", TSqlTokenType.Or, BreakType.NewLine),
            Node("b = 2", spaces: 1)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.None, nodes[1].PrecedingBreak);
        Assert.Equal(BreakType.NewLine, nodes[2].PrecedingBreak);
    }

    [Fact]
    public void Apply_AndOrNewLine_Before_NoChangeFromDmlRules()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                AndOrNewLine = "before"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col1 = 1"),
            Node("AND", TSqlTokenType.And, BreakType.NewLine),
            Node("col2 = 2")
        };

        _rules.Apply(nodes, profile);

        // "before" mode doesn't change the AND break (it was set by LineBreakDecider)
        Assert.Equal(BreakType.NewLine, nodes[1].PrecedingBreak);
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

    // ── DeleteFrom on same line ────────────────────────────────────────────

    [Fact]
    public void Apply_DeleteFromOnSameLine_True_KeepsFromInline()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                DeleteFromOnSameLine = true
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("DELETE", TSqlTokenType.Delete),
            Node("FROM", TSqlTokenType.From, BreakType.NewLine, 0)
        };

        _rules.Apply(nodes, profile);

        // FROM should be kept on same line
        Assert.Equal(BreakType.None, nodes[1].PrecedingBreak);
    }
}
