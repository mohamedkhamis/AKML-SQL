using Xunit;
using AkmlSql.Formatting.Layout;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Layout;

public class IndentationTrackerTests
{
    // ── IndentReason enum ─────────────────────────────────────────────────

    [Fact]
    public void IndentReason_Values()
    {
        Assert.Equal(0, (int)IndentReason.Clause);
        Assert.Equal(1, (int)IndentReason.Parenthesis);
        Assert.Equal(2, (int)IndentReason.BeginEnd);
        Assert.Equal(3, (int)IndentReason.Subquery);
        Assert.Equal(4, (int)IndentReason.CaseExpression);
    }

    // ── Initial state ─────────────────────────────────────────────────────

    [Fact]
    public void InitialDepth_IsZero()
    {
        var tracker = new IndentationTracker();
        Assert.Equal(0, tracker.CurrentDepth);
    }

    [Fact]
    public void InitialReason_IsNull()
    {
        var tracker = new IndentationTracker();
        Assert.Null(tracker.CurrentReason);
    }

    // ── Push / Pop ────────────────────────────────────────────────────────

    [Fact]
    public void Push_IncreasesDepth()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.Parenthesis);
        Assert.Equal(1, tracker.CurrentDepth);
    }

    [Fact]
    public void Push_SetsCurrentReason()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.BeginEnd);
        Assert.Equal(IndentReason.BeginEnd, tracker.CurrentReason);
    }

    [Fact]
    public void Push_MultipleTimes_IncrementsEachTime()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.Parenthesis);
        tracker.Push(IndentReason.BeginEnd);
        tracker.Push(IndentReason.Subquery);
        Assert.Equal(3, tracker.CurrentDepth);
    }

    [Fact]
    public void Pop_DecreasesDepth()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.Parenthesis);
        tracker.Pop();
        Assert.Equal(0, tracker.CurrentDepth);
    }

    [Fact]
    public void Pop_OnEmptyStack_DoesNotGoNegative()
    {
        var tracker = new IndentationTracker();
        tracker.Pop(); // should not throw, depth stays 0
        Assert.Equal(0, tracker.CurrentDepth);
    }

    [Fact]
    public void Pop_RestorestoPreviousDepth()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.Clause);
        tracker.Push(IndentReason.Parenthesis);
        tracker.Pop();
        Assert.Equal(1, tracker.CurrentDepth);
        Assert.Equal(IndentReason.Clause, tracker.CurrentReason);
    }

    // ── Reset ─────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsDepthAndStack()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.BeginEnd);
        tracker.Push(IndentReason.Subquery);
        tracker.Reset();
        Assert.Equal(0, tracker.CurrentDepth);
        Assert.Null(tracker.CurrentReason);
    }

    // ── SetDepth ──────────────────────────────────────────────────────────

    [Fact]
    public void SetDepth_SetsDirectly()
    {
        var tracker = new IndentationTracker();
        tracker.SetDepth(5);
        Assert.Equal(5, tracker.CurrentDepth);
    }

    [Fact]
    public void SetDepth_NegativeValue_ClampsToZero()
    {
        var tracker = new IndentationTracker();
        tracker.SetDepth(-3);
        Assert.Equal(0, tracker.CurrentDepth);
    }

    [Fact]
    public void SetDepth_Zero_SetsToZero()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.Clause);
        tracker.SetDepth(0);
        Assert.Equal(0, tracker.CurrentDepth);
    }

    // ── ProcessToken ──────────────────────────────────────────────────────

    [Fact]
    public void ProcessToken_RightParenthesis_PopsBeforeReturning()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.Parenthesis); // depth=1
        var indent = tracker.ProcessToken(TSqlTokenType.RightParenthesis, ")");
        // Pops first → depth becomes 0, returns 0
        Assert.Equal(0, indent);
        Assert.Equal(0, tracker.CurrentDepth);
    }

    [Fact]
    public void ProcessToken_EndToken_PopsBeforeReturning()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.BeginEnd); // depth=1
        var indent = tracker.ProcessToken(TSqlTokenType.End, "END");
        Assert.Equal(0, indent);
        Assert.Equal(0, tracker.CurrentDepth);
    }

    [Fact]
    public void ProcessToken_EndToken_LowercaseText_Pops()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.BeginEnd);
        var indent = tracker.ProcessToken(TSqlTokenType.End, "end");
        // upperText = "END" → pops
        Assert.Equal(0, indent);
    }

    [Fact]
    public void ProcessToken_LeftParenthesis_PushesAfterReturning()
    {
        var tracker = new IndentationTracker();
        var indent = tracker.ProcessToken(TSqlTokenType.LeftParenthesis, "(");
        // Returns current depth (0) BEFORE push
        Assert.Equal(0, indent);
        Assert.Equal(1, tracker.CurrentDepth);
        Assert.Equal(IndentReason.Parenthesis, tracker.CurrentReason);
    }

    [Fact]
    public void ProcessToken_BeginToken_PushesAfterReturning()
    {
        var tracker = new IndentationTracker();
        var indent = tracker.ProcessToken(TSqlTokenType.Begin, "BEGIN");
        Assert.Equal(0, indent); // returns current depth before push
        Assert.Equal(1, tracker.CurrentDepth);
        Assert.Equal(IndentReason.BeginEnd, tracker.CurrentReason);
    }

    [Fact]
    public void ProcessToken_BeginToken_LowercaseText_Pushes()
    {
        var tracker = new IndentationTracker();
        tracker.ProcessToken(TSqlTokenType.Begin, "begin");
        Assert.Equal(1, tracker.CurrentDepth);
    }

    [Fact]
    public void ProcessToken_OtherToken_ReturnsCurrent_NoChange()
    {
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.Clause); // depth=1
        var indent = tracker.ProcessToken(TSqlTokenType.Select, "SELECT");
        Assert.Equal(1, indent);
        Assert.Equal(1, tracker.CurrentDepth);
    }

    [Fact]
    public void ProcessToken_BeginWithNonBeginText_NoChange()
    {
        // The Beginning token type with text "something" (not "BEGIN")
        // → second switch: `case TSqlTokenType.Begin when upperText == "BEGIN":` doesn't match
        var tracker = new IndentationTracker();
        var indent = tracker.ProcessToken(TSqlTokenType.Begin, "other");
        Assert.Equal(0, indent);
        Assert.Equal(0, tracker.CurrentDepth); // no push
    }

    [Fact]
    public void ProcessToken_EndWithNonEndText_NoChange()
    {
        // End token type with text other than "END"
        var tracker = new IndentationTracker();
        tracker.Push(IndentReason.BeginEnd);
        var indent = tracker.ProcessToken(TSqlTokenType.End, "other");
        // The 'when upperText == "END"' guard doesn't match, falls through to currentIndent=1
        Assert.Equal(1, indent);
        Assert.Equal(1, tracker.CurrentDepth); // no pop
    }

    [Fact]
    public void ProcessToken_MatchingBeginEnd_Roundtrip()
    {
        var tracker = new IndentationTracker();
        Assert.Equal(0, tracker.ProcessToken(TSqlTokenType.Begin, "BEGIN")); // pushes → depth=1, returns 0
        Assert.Equal(1, tracker.ProcessToken(TSqlTokenType.Select, "SELECT")); // returns 1, depth=1
        Assert.Equal(0, tracker.ProcessToken(TSqlTokenType.End, "END")); // pops first → depth=0, returns 0
        Assert.Equal(0, tracker.CurrentDepth);
    }

    // Spec 047 (FMT-002): END also closes a CASE expression, and BEGIN / END also start statements that open or close
    // no block. Counting them as blocks moved every later line of the block a level out (or in).

    [Fact]
    public void A_case_expressions_end_does_not_close_the_enclosing_block()
    {
        var tracker = new IndentationTracker();
        tracker.ProcessToken(TSqlTokenType.Begin, "BEGIN", "SELECT");
        tracker.ProcessToken(TSqlTokenType.Select, "SELECT", "CASE");
        tracker.ProcessToken(TSqlTokenType.Case, "CASE", "WHEN");
        tracker.ProcessToken(TSqlTokenType.When, "WHEN", "1");
        Assert.Equal(1, tracker.ProcessToken(TSqlTokenType.End, "END", "SELECT"));
        Assert.Equal(1, tracker.ProcessToken(TSqlTokenType.Select, "SELECT", "4"));   // still inside the block
        Assert.Equal(0, tracker.ProcessToken(TSqlTokenType.End, "END", null));        // the block's own END
        Assert.Equal(0, tracker.CurrentDepth);
    }

    [Theory]
    [InlineData("TRAN")]
    [InlineData("TRANSACTION")]
    [InlineData("DISTRIBUTED")]
    [InlineData("DIALOG")]
    [InlineData("CONVERSATION")]
    public void A_begin_that_starts_a_statement_opens_no_block(string next)
    {
        var tracker = new IndentationTracker();
        tracker.ProcessToken(TSqlTokenType.Begin, "BEGIN", next);
        Assert.Equal(0, tracker.CurrentDepth);
    }

    [Fact]
    public void End_conversation_closes_no_block()
    {
        var tracker = new IndentationTracker();
        tracker.ProcessToken(TSqlTokenType.Begin, "BEGIN", "END");
        Assert.Equal(1, tracker.ProcessToken(TSqlTokenType.End, "END", "CONVERSATION"));
        Assert.Equal(1, tracker.CurrentDepth);
    }
}
