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

    // ── BETWEEN's AND must not be re-indented; the clause-level AND must (Spec 030 T008) ──

    [Fact]
    public void Apply_AndOrIndent_LeavesBetweenAnd_ReindentsBooleanAnd()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                AndOrNewLine = "before",
                AndOrIndent = "alignWithWhere"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("col", TSqlTokenType.Identifier, indent: 1),
            Node("BETWEEN", TSqlTokenType.Between, indent: 1),
            Node("1", TSqlTokenType.Integer),
            Node("AND", TSqlTokenType.And, BreakType.NewLine, indent: 2),   // BETWEEN's AND
            Node("10", TSqlTokenType.Integer),
            Node("AND", TSqlTokenType.And, BreakType.NewLine, indent: 2),   // clause-level boolean AND
            Node("y = 1", TSqlTokenType.Identifier)
        };

        _rules.Apply(nodes, profile);

        // BETWEEN's AND keeps its indent — it is part of the BETWEEN expression.
        Assert.Equal(2, nodes[3].IndentLevel);
        // The clause-level boolean AND re-aligns with WHERE: alignWithWhere => existing(2) - 1 = 1.
        Assert.Equal(1, nodes[5].IndentLevel);
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
