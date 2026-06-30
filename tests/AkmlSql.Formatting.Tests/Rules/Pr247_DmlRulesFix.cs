using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Rules;

/// <summary>
/// PR #247 regression: expectBetweenAnd flag was not reset when the BETWEEN's AND lands inside
/// a noformat region, causing the next clause-level AND (in normal code after the region) to be
/// silently skipped (treated as BETWEEN's AND) and left un-re-indented.
/// </summary>
public class Pr247_DmlRulesFix
{
    private readonly DmlRules _rules = new();

    private static LayoutNode Node(
        string text,
        TSqlTokenType tokenType = TSqlTokenType.Identifier,
        BreakType breakType = BreakType.None,
        int spaces = 1,
        int indent = 0,
        bool noformat = false)
    {
        return new LayoutNode
        {
            FormattedText = text,
            TokenType = tokenType,
            PrecedingBreak = breakType,
            PrecedingSpaces = spaces,
            IndentLevel = indent,
            IsInNoformatRegion = noformat
        };
    }

    /// <summary>
    /// BETWEEN inside a noformat region — both the BETWEEN and its AND are marked noformat.
    /// The AND that follows (in normal code) is a clause-level boolean AND and must still be
    /// re-indented by AndOrIndent, not silently skipped as a "BETWEEN's AND".
    /// </summary>
    [Fact]
    public void BetweenAndInsideNoformat_DoesNotLeakExpectBetweenAnd_IntoNormalAnd()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                AndOrNewLine = "before",
                AndOrIndent = "alignWithWhere"   // alignWithWhere: existing(2) - 1 = 1
            }
        };

        // Simulate:
        //   col BETWEEN 1 AND 10    -- inside noformat region (all 5 tokens marked noformat)
        //   AND y = 2               -- normal region, clause-level boolean AND at indent 2
        var nodes = new List<LayoutNode>
        {
            Node("col",     TSqlTokenType.Identifier, indent: 1, noformat: true),
            Node("BETWEEN", TSqlTokenType.Between,    indent: 1, noformat: true),
            Node("1",       TSqlTokenType.Integer,               noformat: true),
            Node("AND",     TSqlTokenType.And, BreakType.NewLine, indent: 2, noformat: true),  // BETWEEN's AND, in noformat
            Node("10",      TSqlTokenType.Integer,               noformat: true),
            // Normal region starts here
            Node("AND",     TSqlTokenType.And, BreakType.NewLine, indent: 2),  // clause-level AND
            Node("y = 2",   TSqlTokenType.Identifier)
        };

        _rules.Apply(nodes, profile);

        // The clause-level AND (index 5) must be re-indented: alignWithWhere => 2 - 1 = 1.
        // Before the fix it was silently skipped (expectBetweenAnd leaked from noformat BETWEEN),
        // leaving it at indent 2.
        Assert.Equal(1, nodes[5].IndentLevel);
    }

    /// <summary>
    /// Complementary: BETWEEN inside a noformat region where only BETWEEN is noformat but the AND
    /// is in normal code — the AND should still be treated as BETWEEN's AND (not re-indented).
    /// Also verifies a subsequent clause-level AND IS re-indented.
    /// </summary>
    [Fact]
    public void BetweenInNoformat_AndInNormalRegion_ThenClauseLevelAndReindented()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                AndOrNewLine = "before",
                AndOrIndent = "alignWithWhere"
            }
        };

        // Simulate:
        //   BETWEEN [noformat]
        //   1                   [normal]
        //   AND 10              [normal]  <- BETWEEN's AND, BETWEEN was noformat so flag reset on its way through
        //   AND y = 2           [normal]  <- clause-level AND, must be re-indented
        var nodes = new List<LayoutNode>
        {
            Node("col",     TSqlTokenType.Identifier, indent: 1),
            Node("BETWEEN", TSqlTokenType.Between, indent: 1, noformat: true),  // BETWEEN in noformat → flag reset
            Node("1",       TSqlTokenType.Integer),
            Node("AND",     TSqlTokenType.And, BreakType.NewLine, indent: 2),   // BETWEEN's AND, but flag was reset
            Node("10",      TSqlTokenType.Integer),
            Node("AND",     TSqlTokenType.And, BreakType.NewLine, indent: 2),   // clause-level AND
            Node("y = 2",   TSqlTokenType.Identifier)
        };

        _rules.Apply(nodes, profile);

        // Both ANDs should be re-indented: the BETWEEN flag was reset when BETWEEN was skipped
        // (noformat), so the first AND in normal code is treated as clause-level (indent 2-1=1),
        // and so is the second.
        Assert.Equal(1, nodes[3].IndentLevel);
        Assert.Equal(1, nodes[5].IndentLevel);
    }

    // NOTE: rule-level idempotency is intentionally NOT asserted here. ApplyAndOrIndent computes a
    // relative delta (indent - 1) each pass, so re-applying the rule directly to the SAME node list
    // double-subtracts. Idempotency is a pipeline (Stage-7) property — it re-parses the formatted
    // text and re-runs from scratch, never re-applying on already-mutated nodes — and is covered by
    // the FormatParityTests golden + idempotency suite.
}
