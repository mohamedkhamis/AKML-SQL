using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Rules;

public class CasingRulesTests
{
    private readonly CasingRules _rules = new();

    private static LayoutNode Node(
        string text,
        TSqlTokenType tokenType = TSqlTokenType.Identifier,
        bool inNoformat = false)
    {
        return new LayoutNode
        {
            FormattedText = text,
            TokenType = tokenType,
            PrecedingBreak = BreakType.None,
            PrecedingSpaces = 1,
            IsInNoformatRegion = inNoformat
        };
    }

    // ── Keywords ──────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Keywords_Uppercase_ConvertsSelectToUpper()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "UPPERCASE"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("select", TSqlTokenType.Select)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("SELECT", nodes[0].FormattedText);
    }

    [Fact]
    public void Apply_Keywords_Lowercase_ConvertsSelectToLower()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "lowercase"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("SELECT", TSqlTokenType.Select)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("select", nodes[0].FormattedText);
    }

    [Fact]
    public void Apply_Keywords_AsIs_LeavesTextUnchanged()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "AsIs"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("Select", TSqlTokenType.Select)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("Select", nodes[0].FormattedText);
    }

    // ── Global variables ──────────────────────────────────────────────────

    [Fact]
    public void Apply_GlobalVariables_Uppercase_ConvertsRowcount()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                GlobalVariables = "UPPERCASE"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("@@rowcount", TSqlTokenType.Variable)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("@@ROWCOUNT", nodes[0].FormattedText);
    }

    [Fact]
    public void Apply_GlobalVariables_Lowercase_ConvertsRowcount()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                GlobalVariables = "lowercase"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("@@ROWCOUNT", TSqlTokenType.Variable)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("@@rowcount", nodes[0].FormattedText);
    }

    // ── Local variables ───────────────────────────────────────────────────

    [Fact]
    public void Apply_LocalVariables_Lowercase_ConvertsVar()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                LocalVariables = "lowercase"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("@MyVar", TSqlTokenType.Variable)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("@myvar", nodes[0].FormattedText);
    }

    [Fact]
    public void Apply_LocalVariables_Uppercase_ConvertsVar()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                LocalVariables = "UPPERCASE"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("@myvar", TSqlTokenType.Variable)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("@MYVAR", nodes[0].FormattedText);
    }

    [Fact]
    public void Apply_LocalVariables_AsIs_Unchanged()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                LocalVariables = "AsIs"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("@MyVar", TSqlTokenType.Variable)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("@MyVar", nodes[0].FormattedText);
    }

    // ── System objects ────────────────────────────────────────────────────

    [Fact]
    public void Apply_SystemObjects_Lowercase_ConvertsSpHelp()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                SystemObjects = "lowercase"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("SP_HELP", TSqlTokenType.Identifier)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("sp_help", nodes[0].FormattedText);
    }

    [Fact]
    public void Apply_SystemObjects_Uppercase_ConvertsSys()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                SystemObjects = "UPPERCASE"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("sys", TSqlTokenType.Identifier)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("SYS", nodes[0].FormattedText);
    }

    // ── Noformat region ───────────────────────────────────────────────────

    [Fact]
    public void Apply_NoformatRegion_Skipped()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "UPPERCASE",
                GlobalVariables = "UPPERCASE",
                LocalVariables = "UPPERCASE"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("select", TSqlTokenType.Select, inNoformat: true),
            Node("@@rowcount", TSqlTokenType.Variable, inNoformat: true),
            Node("@myvar", TSqlTokenType.Variable, inNoformat: true)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("select", nodes[0].FormattedText);
        Assert.Equal("@@rowcount", nodes[1].FormattedText);
        Assert.Equal("@myvar", nodes[2].FormattedText);
    }

    // ── Empty list ────────────────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyList_NoThrow()
    {
        var profile = new FormattingProfile();
        var nodes = new List<LayoutNode>();
        var ex = Record.Exception(() => _rules.Apply(nodes, profile));
        Assert.Null(ex);
    }

    // ── String/comment tokens untouched ──────────────────────────────────

    [Fact]
    public void Apply_StringLiteral_Unchanged()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "UPPERCASE"
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("'hello'", TSqlTokenType.AsciiStringLiteral)
        };

        _rules.Apply(nodes, profile);

        Assert.Equal("'hello'", nodes[0].FormattedText);
    }
}
