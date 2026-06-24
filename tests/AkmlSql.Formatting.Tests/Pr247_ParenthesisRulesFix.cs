using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests;

/// <summary>
/// Regression tests for PR #247: ApplyProcedureParameters depth-unaware comma scan.
/// A comma nested inside a type argument (e.g. decimal(10, 2)) must NOT be treated as
/// a parameter separator — only depth-0 commas are parameter boundaries.
/// </summary>
public class Pr247_ParenthesisRulesFix
{
    private readonly ParenthesisRules _rules = new();

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

    // ── ApplyProcedureParameters: depth-aware comma fix ────────────────────

    /// <summary>
    /// CREATE PROCEDURE dbo.Foo ( @Amount decimal(10, 2), @Name nvarchar(50) )
    ///
    /// The comma inside decimal(10, 2) is at depth 1 relative to the procedure
    /// parameter list. It must NOT get a NewLine break injected. Only the comma
    /// between @Amount and @Name (depth 0) should introduce a break.
    /// </summary>
    [Fact]
    public void ProcedureParameters_NewLine_InnerTypeCommaNotSplit()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                ProcedureParameters = "newLine",
                CollapseShort = false
            }
        };

        // Represents: PROCEDURE dbo . Foo ( @Amount decimal ( 10 , 2 ) , @Name nvarchar ( 50 ) )
        // Indices:      0        1  2  3   4       5       6  7   8  9  10     11      12 13  14 15
        var nodes = new List<LayoutNode>
        {
            // PROCEDURE dbo.Foo
            Node("PROCEDURE", TSqlTokenType.Procedure),
            Node("dbo"),
            Node(".", TSqlTokenType.Dot),
            Node("Foo"),
            // outer open paren — this is the proc param list paren
            Node("(", TSqlTokenType.LeftParenthesis),
            // @Amount decimal(10, 2)
            Node("@Amount"),
            Node("decimal"),
            Node("(", TSqlTokenType.LeftParenthesis),   // depth → 1
            Node("10"),
            Node(",", TSqlTokenType.Comma),              // ← nested comma at depth 1 (BUG target)
            Node("2"),
            Node(")", TSqlTokenType.RightParenthesis),  // depth → 0
            // separator comma at depth 0
            Node(",", TSqlTokenType.Comma),              // ← should trigger NewLine on next
            // @Name nvarchar(50)
            Node("@Name"),
            Node("nvarchar"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("50"),
            Node(")", TSqlTokenType.RightParenthesis),
            // outer close paren
            Node(")", TSqlTokenType.RightParenthesis),
        };

        _rules.Apply(nodes, profile);

        // The node after the depth-1 (nested) comma — "2" at index 10 — must NOT have a break.
        var nodeAfterNestedComma = nodes[10]; // "2"
        Assert.Equal(BreakType.None, nodeAfterNestedComma.PrecedingBreak);

        // The node after the depth-0 (parameter separator) comma — "@Name" at index 13 — MUST have a break.
        var nodeAfterParamComma = nodes[13]; // "@Name"
        Assert.Equal(BreakType.NewLine, nodeAfterParamComma.PrecedingBreak);
        Assert.Equal(1, nodeAfterParamComma.IndentLevel);
        Assert.Equal(0, nodeAfterParamComma.PrecedingSpaces);
    }

    /// <summary>
    /// Idempotency: applying the rule twice to the same node list must not change
    /// the break state of any node. A pre-existing NewLine break must not be promoted
    /// or duplicated, and the nested comma must remain unbroken after both passes.
    /// </summary>
    [Fact]
    public void ProcedureParameters_NewLine_Idempotent()
    {
        var profile = new FormattingProfile
        {
            Parenthesis =
            {
                ProcedureParameters = "newLine",
                CollapseShort = false
            }
        };

        var nodes = new List<LayoutNode>
        {
            Node("PROCEDURE", TSqlTokenType.Procedure),
            Node("dbo"),
            Node(".", TSqlTokenType.Dot),
            Node("MyProc"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("@Price"),
            Node("decimal"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("18"),
            Node(",", TSqlTokenType.Comma),              // nested comma
            Node("4"),
            Node(")", TSqlTokenType.RightParenthesis),
            Node(",", TSqlTokenType.Comma),              // param separator
            Node("@Label"),
            Node("nvarchar"),
            Node("(", TSqlTokenType.LeftParenthesis),
            Node("100"),
            Node(")", TSqlTokenType.RightParenthesis),
            Node(")", TSqlTokenType.RightParenthesis),
        };

        // First pass
        _rules.Apply(nodes, profile);

        // Snapshot break states after first pass
        var breaksAfterFirstPass = nodes.Select(n => n.PrecedingBreak).ToList();

        // Second pass
        _rules.Apply(nodes, profile);

        // All break states must be identical after the second pass
        for (int i = 0; i < nodes.Count; i++)
        {
            Assert.Equal(breaksAfterFirstPass[i], nodes[i].PrecedingBreak);
        }

        // Also verify the nested comma's successor is still unbroken
        var nodeAfterNestedComma = nodes[10]; // "4"
        Assert.Equal(BreakType.None, nodeAfterNestedComma.PrecedingBreak);
    }
}
