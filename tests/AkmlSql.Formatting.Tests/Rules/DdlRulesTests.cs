using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Rules;

public class DdlRulesTests
{
    private readonly DdlRules _rules = new();

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

    // ── CreateTableColumnsOnNewLine ───────────────────────────────────────

    [Fact]
    public void Apply_CreateTableColumnsOnNewLine_True_BreaksFirstColumn()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                CreateTableColumnsOnNewLine = true,
                CollapseShortDdl = false
            }
        };

        // CREATE TABLE t ( id INT )
        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("TABLE", TSqlTokenType.Table),
            Node("t"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("id"),
            Node("INT"),
            Node(")", TSqlTokenType.RightParenthesis)
        };

        _rules.Apply(nodes, profile);

        // First column (nodes[4]) should be on a new line
        Assert.Equal(BreakType.NewLine, nodes[4].PrecedingBreak);
    }

    [Fact]
    public void Apply_CreateTableColumnsOnNewLine_True_BreaksAfterComma()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                CreateTableColumnsOnNewLine = true,
                CollapseShortDdl = false
            }
        };

        // CREATE TABLE t ( id INT , name VARCHAR )
        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("TABLE", TSqlTokenType.Table),
            Node("t"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("id"),
            Node("INT"),
            Node(",", TSqlTokenType.Comma),
            Node("name"),
            Node("VARCHAR"),
            Node(")", TSqlTokenType.RightParenthesis)
        };

        _rules.Apply(nodes, profile);

        // Node after comma (nodes[7]) should be on a new line
        Assert.Equal(BreakType.NewLine, nodes[7].PrecedingBreak);
    }

    [Fact]
    public void Apply_CreateTableColumnsOnNewLine_False_NoChange()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                CreateTableColumnsOnNewLine = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("TABLE", TSqlTokenType.Table),
            Node("t"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("id"),
            Node("INT"),
            Node(")", TSqlTokenType.RightParenthesis)
        };

        _rules.Apply(nodes, profile);

        // No break added
        Assert.Equal(BreakType.None, nodes[4].PrecedingBreak);
    }

    // ── AsOnNewLine ──────────────────────────────────────────────────────

    [Fact]
    public void Apply_AsOnNewLine_True_AddsBreakAfterProcedure()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                AsOnNewLine = true,
                CollapseShortDdl = false
            }
        };

        // CREATE PROCEDURE p AS
        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("PROCEDURE", TSqlTokenType.Procedure),
            Node("p"),
            Node("AS", TSqlTokenType.As)  // should get NewLine
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[3].PrecedingBreak);
    }

    [Fact]
    public void Apply_AsOnNewLine_False_NoBreakAdded()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                AsOnNewLine = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("PROCEDURE", TSqlTokenType.Procedure),
            Node("p"),
            Node("AS", TSqlTokenType.As)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.None, nodes[3].PrecedingBreak);
    }

    // ── BeginOnNewLine ────────────────────────────────────────────────────

    [Fact]
    public void Apply_BeginOnNewLine_True_AddsBreakAfterAs()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                BeginOnNewLine = true,
                CollapseShortDdl = false
            }
        };

        // CREATE PROCEDURE p AS BEGIN ...
        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("PROCEDURE", TSqlTokenType.Procedure),
            Node("p"),
            Node("AS", TSqlTokenType.As),
            Node("BEGIN", TSqlTokenType.Begin)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[4].PrecedingBreak);
    }

    [Fact]
    public void Apply_BeginOnNewLine_False_NoBreakAdded()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                BeginOnNewLine = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("PROCEDURE", TSqlTokenType.Procedure),
            Node("p"),
            Node("AS", TSqlTokenType.As),
            Node("BEGIN", TSqlTokenType.Begin)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.None, nodes[4].PrecedingBreak);
    }

    // ── CollapseShortDdl ──────────────────────────────────────────────────

    [Fact]
    public void Apply_CollapseShortDdl_True_ShortStatementCollapsed()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                CollapseShortDdl = true,
                CollapseThreshold = 100
            }
        };

        // CREATE TABLE t ( id INT )
        // but columns on new lines — should be collapsed since it's short
        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("TABLE", TSqlTokenType.Table),
            Node("t"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("id", breakType: BreakType.NewLine, indent: 1, spaces: 0),
            Node("INT"),
            Node(")", TSqlTokenType.RightParenthesis, breakType: BreakType.NewLine)
        };

        _rules.Apply(nodes, profile);

        // All breaks should be removed (collapsed to single line)
        Assert.Equal(BreakType.None, nodes[4].PrecedingBreak);
        Assert.Equal(BreakType.None, nodes[6].PrecedingBreak);
    }

    [Fact]
    public void Apply_CollapseShortDdl_False_BreaksPreserved()
    {
        var profile = new FormattingProfile
        {
            Ddl =
            {
                CollapseShortDdl = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("CREATE", TSqlTokenType.Create),
            Node("TABLE", TSqlTokenType.Table),
            Node("t"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("id", breakType: BreakType.NewLine, indent: 1, spaces: 0),
            Node(")", TSqlTokenType.RightParenthesis, breakType: BreakType.NewLine)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal(BreakType.NewLine, nodes[4].PrecedingBreak);
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
