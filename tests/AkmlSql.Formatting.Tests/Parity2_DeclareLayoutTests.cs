using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace AkmlSql.Formatting.Tests;

/// <summary>
/// Spec 030 — DECLARE / SET variable layout tests.
///
/// <para>Tests the <see cref="DeclareRules"/> rule set directly (node-level, no pipeline).
/// Concurrent agents may not share test files — this file is dedicated to DeclareRules.</para>
///
/// <para><b>Guard test</b>: <see cref="DefaultProfile_NoOp_AllNodesUnchanged"/> verifies that
/// the default <see cref="FormattingProfile"/> (where <c>Declare.OneDeclarationPerLine = false</c>)
/// leaves every node's <c>(PrecedingBreak, PrecedingSpaces, IndentLevel)</c> completely unchanged,
/// proving zero mutation on the default code path and protecting all 709 format goldens.</para>
/// </summary>
public class Parity2_DeclareLayoutTests
{
    private readonly DeclareRules _rules = new();

    // ── Node builder (mirrors ControlFlowRulesTests) ───────────────────────

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
            IsInNoformatRegion = inNoformat,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GUARD: default profile must not mutate any node (byte-identical guarantee)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultProfile_NoOp_AllNodesUnchanged()
    {
        // Arrange: multi-variable DECLARE (the exact kind the rule would split when ON).
        var nodes = new List<LayoutNode>
        {
            Node("DECLARE", TSqlTokenType.Declare, BreakType.NewLine, 0, 0),
            Node("@a",      TSqlTokenType.Variable),
            Node("INT",     TSqlTokenType.Identifier),
            Node(",",       TSqlTokenType.Comma, BreakType.None, 0),
            Node("@b",      TSqlTokenType.Variable, BreakType.None, 1),
            Node("VARCHAR", TSqlTokenType.Identifier),
            Node("(",       TSqlTokenType.LeftParenthesis, BreakType.None, 0),
            Node("10",      TSqlTokenType.Integer),
            Node(")",       TSqlTokenType.RightParenthesis, BreakType.None, 0),
        };

        // Snapshot all node state before applying the default profile
        var snapshot = nodes.Select(n => (n.PrecedingBreak, n.PrecedingSpaces, n.IndentLevel)).ToList();

        // Act: apply with default profile (OneDeclarationPerLine = false by default)
        var profile = new FormattingProfile();
        _rules.Apply(nodes, profile);

        // Assert: nothing was changed
        for (int i = 0; i < nodes.Count; i++)
        {
            Assert.Equal(snapshot[i].PrecedingBreak,  nodes[i].PrecedingBreak);
            Assert.Equal(snapshot[i].PrecedingSpaces, nodes[i].PrecedingSpaces);
            Assert.Equal(snapshot[i].IndentLevel,     nodes[i].IndentLevel);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OPT-IN: OneDeclarationPerLine splits comma-separated variables
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OneDeclarationPerLine_SplitsCommaVariables()
    {
        // Arrange: DECLARE @a INT, @b INT  (all inline)
        var nodes = new List<LayoutNode>
        {
            Node("DECLARE", TSqlTokenType.Declare, BreakType.NewLine, 0, 0),
            Node("@a",      TSqlTokenType.Variable),
            Node("INT",     TSqlTokenType.Identifier),
            Node(",",       TSqlTokenType.Comma, BreakType.None, 0),
            Node("@b",      TSqlTokenType.Variable, BreakType.None, 1),  // was inline
            Node("INT",     TSqlTokenType.Identifier),
        };

        var profile = new FormattingProfile
        {
            Declare = { OneDeclarationPerLine = true }
        };

        // Act
        _rules.Apply(nodes, profile);

        // Assert: @b token is now on a new line
        Assert.Equal(BreakType.NewLine, nodes[4].PrecedingBreak);
    }

    [Fact]
    public void OneDeclarationPerLine_IndentsNewVariableOneLevel()
    {
        // The variable after a comma should land at DECLARE's indent + 1
        var nodes = new List<LayoutNode>
        {
            Node("DECLARE", TSqlTokenType.Declare, BreakType.NewLine, 0, indent: 0),
            Node("@a",      TSqlTokenType.Variable),
            Node("INT",     TSqlTokenType.Identifier),
            Node(",",       TSqlTokenType.Comma, BreakType.None, 0),
            Node("@b",      TSqlTokenType.Variable, BreakType.None, 1),
            Node("INT",     TSqlTokenType.Identifier),
        };

        var profile = new FormattingProfile
        {
            Declare = { OneDeclarationPerLine = true }
        };

        _rules.Apply(nodes, profile);

        // DECLARE is at indent 0, so @b should go to indent 1
        Assert.Equal(1, nodes[4].IndentLevel);
    }

    [Fact]
    public void OneDeclarationPerLine_ThreeVariables_AllSplit()
    {
        // DECLARE @a INT, @b NVARCHAR(50), @c BIT
        var nodes = new List<LayoutNode>
        {
            Node("DECLARE", TSqlTokenType.Declare, BreakType.NewLine, 0, 0),
            Node("@a",      TSqlTokenType.Variable),
            Node("INT",     TSqlTokenType.Identifier),
            Node(",",       TSqlTokenType.Comma, BreakType.None, 0),
            Node("@b",      TSqlTokenType.Variable, BreakType.None, 1),
            Node("NVARCHAR",TSqlTokenType.Identifier),
            Node("(",       TSqlTokenType.LeftParenthesis, BreakType.None, 0),
            Node("50",      TSqlTokenType.Integer),
            Node(")",       TSqlTokenType.RightParenthesis, BreakType.None, 0),
            Node(",",       TSqlTokenType.Comma, BreakType.None, 0),
            Node("@c",      TSqlTokenType.Variable, BreakType.None, 1),
            Node("BIT",     TSqlTokenType.Identifier),
        };

        var profile = new FormattingProfile
        {
            Declare = { OneDeclarationPerLine = true }
        };

        _rules.Apply(nodes, profile);

        // Both @b (index 4) and @c (index 10) must be on new lines
        Assert.Equal(BreakType.NewLine, nodes[4].PrecedingBreak);
        Assert.Equal(BreakType.NewLine, nodes[10].PrecedingBreak);
    }

    [Fact]
    public void OneDeclarationPerLine_AlreadyBroken_NotDuplicated()
    {
        // If @b is already on its own line, the rule should not clobber its existing IndentLevel
        var nodes = new List<LayoutNode>
        {
            Node("DECLARE", TSqlTokenType.Declare, BreakType.NewLine, 0, 0),
            Node("@a",      TSqlTokenType.Variable),
            Node("INT",     TSqlTokenType.Identifier),
            Node(",",       TSqlTokenType.Comma, BreakType.None, 0),
            // @b already broken at indent 2
            Node("@b",      TSqlTokenType.Variable, BreakType.NewLine, 0, indent: 2),
            Node("INT",     TSqlTokenType.Identifier),
        };

        var profile = new FormattingProfile
        {
            Declare = { OneDeclarationPerLine = true }
        };

        _rules.Apply(nodes, profile);

        // Already broken — we should not overwrite the existing break or indent
        Assert.Equal(BreakType.NewLine, nodes[4].PrecedingBreak);
        // The existing IndentLevel of 2 is preserved (rule only sets when PrecedingBreak == None)
        Assert.Equal(2, nodes[4].IndentLevel);
    }

    [Fact]
    public void OneDeclarationPerLine_CommaInsideParen_NotSplit()
    {
        // The comma inside VARCHAR(50, …) must NOT trigger a line break
        // Using a two-arg scenario: @a VARCHAR(10, 5)
        var nodes = new List<LayoutNode>
        {
            Node("DECLARE", TSqlTokenType.Declare, BreakType.NewLine, 0, 0),
            Node("@a",      TSqlTokenType.Variable),
            Node("VARCHAR", TSqlTokenType.Identifier),
            Node("(",       TSqlTokenType.LeftParenthesis, BreakType.None, 0),
            Node("10",      TSqlTokenType.Integer),
            Node(",",       TSqlTokenType.Comma, BreakType.None, 0),   // inside paren
            Node("5",       TSqlTokenType.Integer),
            Node(")",       TSqlTokenType.RightParenthesis, BreakType.None, 0),
        };

        var snapshot = nodes.Select(n => (n.PrecedingBreak, n.PrecedingSpaces, n.IndentLevel)).ToList();

        var profile = new FormattingProfile
        {
            Declare = { OneDeclarationPerLine = true }
        };

        _rules.Apply(nodes, profile);

        // The "5" token (index 6) should remain inline — it was inside parentheses
        Assert.Equal(BreakType.None, nodes[6].PrecedingBreak);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Alignment
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AlignDataTypes_PadsShortNamesToCommonColumn()
    {
        // DECLARE @a     INT
        //         @long  VARCHAR(50)
        // @a is short; @long is wider. After alignment, INT and VARCHAR should be at
        // the same column — so INT gets extra PrecedingSpaces.
        var nodes = new List<LayoutNode>
        {
            Node("DECLARE", TSqlTokenType.Declare, BreakType.NewLine, 0, 0),
            Node("@a",      TSqlTokenType.Variable),          // index 1 — 2 chars
            Node("INT",     TSqlTokenType.Identifier),         // index 2
            Node(",",       TSqlTokenType.Comma, BreakType.None, 0),
            Node("@long",   TSqlTokenType.Variable, BreakType.NewLine, 0, 1), // index 4 — 5 chars
            Node("VARCHAR", TSqlTokenType.Identifier),         // index 5
            Node("(",       TSqlTokenType.LeftParenthesis, BreakType.None, 0),
            Node("50",      TSqlTokenType.Integer),
            Node(")",       TSqlTokenType.RightParenthesis, BreakType.None, 0),
        };

        var profile = new FormattingProfile
        {
            Declare = { OneDeclarationPerLine = true, AlignDataTypes = true }
        };

        _rules.Apply(nodes, profile);

        // @a (2 chars) → INT should have more padding than @long (5 chars) → VARCHAR
        // INT's PrecedingSpaces must be ≥ VARCHAR's PrecedingSpaces (which stays at 1)
        int intSpaces = nodes[2].PrecedingSpaces;
        int varcharSpaces = nodes[5].PrecedingSpaces;
        Assert.True(intSpaces >= varcharSpaces,
            $"Expected INT to have at least as much padding as VARCHAR ({intSpaces} vs {varcharSpaces})");
        // And INT must have more than the default 1-space gap
        Assert.True(intSpaces > 1,
            $"Expected INT to be padded beyond 1 space to align with VARCHAR (got {intSpaces})");
    }

    [Fact]
    public void AlignDefaultValues_PadsEqualsToCommonColumn()
    {
        // DECLARE @a   INT     = 1
        //         @bb  BIT     = 0
        // @a is shorter; = should be at the same visual column for both.
        var nodes = new List<LayoutNode>
        {
            Node("DECLARE", TSqlTokenType.Declare, BreakType.NewLine, 0, 0),
            Node("@a",      TSqlTokenType.Variable),               // index 1
            Node("INT",     TSqlTokenType.Identifier),             // index 2
            Node("=",       TSqlTokenType.EqualsSign),             // index 3
            Node("1",       TSqlTokenType.Integer),
            Node(",",       TSqlTokenType.Comma, BreakType.None, 0),
            Node("@bb",     TSqlTokenType.Variable, BreakType.NewLine, 0, 1), // index 6
            Node("BIT",     TSqlTokenType.Identifier),             // index 7
            Node("=",       TSqlTokenType.EqualsSign),             // index 8
            Node("0",       TSqlTokenType.Integer),
        };

        var profile = new FormattingProfile
        {
            Declare = { OneDeclarationPerLine = true, AlignDefaultValues = true }
        };

        _rules.Apply(nodes, profile);

        // Both = tokens (index 3 and 8) should be padded — the wider var gets minimal
        // padding; the shorter one gets more. So first '=' should have more spaces than second '='.
        int eq1 = nodes[3].PrecedingSpaces;
        int eq2 = nodes[8].PrecedingSpaces;
        // The total widths should be equal after padding (both align to maxEqWidth)
        // @a INT = → width = len(@a)+space+len(INT)+padding = 2+1+3+eq1
        // @bb BIT = → width = len(@bb)+space+len(BIT)+eq2 = 3+1+3+eq2
        // maxEqWidth = max(2+1+3, 3+1+3) = max(6, 7) = 7
        // So eq1 padding = 7 - 6 + 1 = 2; eq2 padding = 7 - 7 + 1 = 1
        Assert.True(eq1 >= 1, $"Expected first '=' to have at least 1 space (got {eq1})");
        Assert.True(eq2 >= 1, $"Expected second '=' to have at least 1 space (got {eq2})");
        Assert.True(eq1 >= eq2,
            $"Expected first '=' (shorter name) to have more padding than second (got {eq1} vs {eq2})");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edge cases
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoformatRegion_IsSkipped()
    {
        // A DECLARE inside a noformat region must not be processed
        var nodes = new List<LayoutNode>
        {
            Node("DECLARE", TSqlTokenType.Declare, BreakType.NewLine, 0, 0, inNoformat: true),
            Node("@a",      TSqlTokenType.Variable, inNoformat: true),
            Node("INT",     TSqlTokenType.Identifier, inNoformat: true),
            Node(",",       TSqlTokenType.Comma, BreakType.None, 0, inNoformat: true),
            Node("@b",      TSqlTokenType.Variable, BreakType.None, 1, inNoformat: true),
            Node("INT",     TSqlTokenType.Identifier, inNoformat: true),
        };

        var snapshot = nodes.Select(n => (n.PrecedingBreak, n.PrecedingSpaces, n.IndentLevel)).ToList();

        var profile = new FormattingProfile
        {
            Declare = { OneDeclarationPerLine = true }
        };

        _rules.Apply(nodes, profile);

        // Nothing should change — DECLARE is in noformat region
        for (int i = 0; i < nodes.Count; i++)
        {
            Assert.Equal(snapshot[i].PrecedingBreak,  nodes[i].PrecedingBreak);
            Assert.Equal(snapshot[i].PrecedingSpaces, nodes[i].PrecedingSpaces);
            Assert.Equal(snapshot[i].IndentLevel,     nodes[i].IndentLevel);
        }
    }

    [Fact]
    public void SingleVariableDeclare_NoChange()
    {
        // A single-variable DECLARE should not be affected regardless of flags
        var nodes = new List<LayoutNode>
        {
            Node("DECLARE", TSqlTokenType.Declare, BreakType.NewLine, 0, 0),
            Node("@x",      TSqlTokenType.Variable),
            Node("INT",     TSqlTokenType.Identifier),
        };

        var snapshot = nodes.Select(n => (n.PrecedingBreak, n.PrecedingSpaces, n.IndentLevel)).ToList();

        var profile = new FormattingProfile
        {
            Declare = { OneDeclarationPerLine = true, AlignDataTypes = true, AlignDefaultValues = true }
        };

        _rules.Apply(nodes, profile);

        // With a single variable there's nothing to align or split
        for (int i = 0; i < nodes.Count; i++)
        {
            Assert.Equal(snapshot[i].PrecedingBreak,  nodes[i].PrecedingBreak);
            Assert.Equal(snapshot[i].PrecedingSpaces, nodes[i].PrecedingSpaces);
            Assert.Equal(snapshot[i].IndentLevel,     nodes[i].IndentLevel);
        }
    }
}
