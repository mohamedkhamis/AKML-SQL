using System.Linq;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// Spec 041 (T079, research R17–R19, R23) — the execute reply's aggregation rules, without a database:
/// status, the first error, SSMS-worded legacy messages, row counts per set and DML totals, the caps, and
/// the batch lines SSMS prints.
/// </summary>
public class ExecuteOutcomeTests
{
    private static SqlBatchSpan Span(int index, int startLine = 1, int repeat = 1) =>
        new(index, index * 10, index * 10 + 9, startLine, repeat, "SELECT 1;");

    private static ExecuteResultSet Set(int rows = 1, bool truncated = false) => new()
    {
        ColumnNames = new[] { "n" },
        Rows = Enumerable.Range(0, rows).Select(i => new string?[] { i.ToString() }).ToArray(),
        Truncated = truncated,
    };

    [Fact]
    public void A_script_without_errors_is_Ok_with_no_error_message()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        o.BeginSet(Set());
        o.AddRowCount(1);
        o.EndBatch(5);

        var r = o.Build(123);

        Assert.Equal(ExecuteStatus.Ok, r.Status);
        Assert.Null(r.ErrorMessage);
        Assert.Equal(123, r.CompletedAtUnixMs);
        Assert.Equal(5, Assert.Single(r.Batches).ElapsedMs);
    }

    [Fact]
    public void Any_error_makes_the_script_Error_and_the_first_error_is_the_message_while_later_batches_still_count()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        o.BeginSet(Set());
        o.EndBatch(1);
        o.BeginBatch(Span(1, startLine: 3), 1);
        o.AddServerMessage(208, 16, 1, 1, null, "Invalid object name 'nope'.");
        o.AddServerMessage(102, 15, 1, 2, null, "Incorrect syntax near 'x'.");
        o.EndBatch(1);
        o.BeginBatch(Span(2, startLine: 5), 1);
        o.BeginSet(Set());
        o.EndBatch(1);

        var r = o.Build(0);

        Assert.Equal(ExecuteStatus.Error, r.Status);
        Assert.Equal("Invalid object name 'nope'.", r.ErrorMessage);
        Assert.Equal(2, r.ResultSets.Length);
        Assert.Equal(new[] { 0, 2 }, r.ResultSets.Select(s => s.BatchIndex));
        Assert.Equal(new[] { ExecuteStatus.Ok, ExecuteStatus.Error, ExecuteStatus.Ok }, r.Batches.Select(b => b.Status));
        Assert.Equal(2, r.Batches[1].ErrorCount);
        Assert.Equal(3, r.Batches[1].StartLine);
        Assert.All(r.MessageDetails, m => Assert.Equal(1, m.BatchIndex));
    }

    [Fact]
    public void A_low_severity_message_is_Info_and_does_not_fail_the_script()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        o.AddServerMessage(0, 0, 1, 1, null, "printed");
        o.AddServerMessage(50000, 10, 1, 2, null, "an informational RAISERROR");

        var r = o.Build(0);

        Assert.Equal(ExecuteStatus.Ok, r.Status);
        Assert.All(r.MessageDetails, m => Assert.Equal(ExecuteMessageKind.Info, m.Kind));
        Assert.Equal(new[] { "printed", "an informational RAISERROR" }, r.Messages);
    }

    [Theory]
    [InlineData(ExecuteStatus.TimedOut)]
    [InlineData(ExecuteStatus.Cancelled)]
    public void A_stopped_script_reports_why_it_stopped_even_after_an_error(int status)
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        o.AddServerMessage(208, 16, 1, 1, null, "bad");
        o.EndBatch(1);
        o.BeginBatch(Span(1), 1);
        o.Stop(status);
        o.SkipAfterStop(new[] { Span(2) });

        var r = o.Build(0);

        Assert.Equal(status, r.Status);
        Assert.Equal("bad", r.ErrorMessage);
        Assert.Equal(new[] { ExecuteStatus.Error, status, ExecuteStatus.Skipped }, r.Batches.Select(b => b.Status));
        Assert.True(o.Stopped);
    }

    [Fact]
    public void The_legacy_messages_are_worded_as_in_SSMS()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        o.AddServerMessage(8134, 16, 1, 2, null, "Divide by zero error encountered.");
        o.AddServerMessage(50000, 16, 1, 3, "dbo.p", "boom");
        o.AddRowCount(3);

        var r = o.Build(0);

        Assert.Equal(new[]
        {
            "Msg 8134, Level 16, State 1, Line 2\nDivide by zero error encountered.",
            "Msg 50000, Level 16, State 1, Procedure dbo.p, Line 3\nboom",
            "(3 rows affected)",
        }, r.Messages);
        Assert.Equal("dbo.p", r.MessageDetails[1].Procedure);
        Assert.Null(r.MessageDetails[0].Procedure);
    }

    [Fact]
    public void A_SELECTs_count_belongs_to_its_set_and_DML_counts_sum_into_the_total()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        o.AddRowCount(4);                  // UPDATE before any set
        o.BeginSet(Set(rows: 2));
        o.AddRowCount(2);                  // the SELECT's own count
        o.AddRowCount(7);                  // a DELETE after it
        o.BeginSet(Set(rows: 1));
        o.AddRowCount(1);

        var r = o.Build(0);

        var counts = r.MessageDetails.Where(m => m.Kind == ExecuteMessageKind.RowCount).ToArray();
        Assert.Equal(new[] { -1, 0, -1, 1 }, counts.Select(m => m.ResultSetIndex));
        Assert.Equal(new long[] { 4, 2, 7, 1 }, counts.Select(m => m.RowCount));
        Assert.Equal(11, r.TotalRowsAffected);
        Assert.Equal("(1 row affected)", r.Messages.Last());
    }

    [Fact]
    public void Without_any_DML_count_the_total_is_minus_one()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        o.BeginSet(Set());
        o.AddRowCount(1);

        Assert.Equal(-1, o.Build(0).TotalRowsAffected);
    }

    [Fact]
    public void A_truncated_sets_count_reads_rows_returned_more_exist_and_keeps_the_server_count()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        o.BeginSet(Set(rows: 1000, truncated: true));
        o.AddRowCount(25_000);

        var r = o.Build(0);

        Assert.Equal("(1000 rows returned; more exist)", r.Messages.Single());
        Assert.Equal(25_000, r.MessageDetails.Single().RowCount);
    }

    [Fact]
    public void Messages_beyond_the_cap_are_counted_and_summarised_once()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        for (int i = 0; i < ExecuteOutcome.MaxMessages + 2_000; i++) o.AddServerMessage(0, 0, 1, 1, null, "p");

        var r = o.Build(0);

        Assert.Equal(2_000, r.MessagesOmitted);
        Assert.Equal(ExecuteOutcome.MaxMessages + 1, r.MessageDetails.Length);
        var summary = r.MessageDetails.Last();
        Assert.Equal(ExecuteMessageKind.System, summary.Kind);
        Assert.Contains("2,000", summary.Text);
        Assert.Equal(r.MessageDetails.Length, r.Messages.Length);
    }

    [Fact]
    public void Sets_beyond_the_cap_are_not_kept()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        for (int i = 0; i < ExecuteOutcome.MaxResultSets; i++) Assert.Equal(i, o.BeginSet(Set()));

        Assert.Equal(-1, o.BeginSet(Set()));
        o.AddRowCount(1);   // that set's count belongs to no kept set

        var r = o.Build(0);
        Assert.Equal(ExecuteOutcome.MaxResultSets, r.ResultSets.Length);
        Assert.Equal(1, r.TotalRowsAffected);
    }

    [Fact]
    public void A_repeat_count_over_the_limit_is_clamped_with_a_system_line()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);

        Assert.Equal(3, o.ClampRepeat(3));
        Assert.Equal(ExecuteOutcome.MaxBatchRepeat, o.ClampRepeat(50_000));

        var m = o.Build(0).MessageDetails.Single();
        Assert.Equal(ExecuteMessageKind.System, m.Kind);
        Assert.Contains("50000", m.Text);
    }

    [Fact]
    public void A_loop_prints_SSMS_lines_before_and_after()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0, repeat: 3), 3);
        o.BeginLoop();
        for (int i = 0; i < 3; i++) { o.BeginSet(Set()); o.AddRowCount(1); }
        o.EndLoop(3);

        var r = o.Build(0);

        Assert.Equal("Beginning execution loop", r.Messages.First());
        Assert.Equal("Batch execution completed 3 times.", r.Messages.Last());
        Assert.Equal(3, r.Batches.Single().RepeatCount);
        Assert.Equal(3, r.ResultSets.Length);
    }

    [Fact]
    public void After_the_connection_closes_the_remaining_batches_are_skipped_with_SSMS_line()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        o.AddServerMessage(2745, 20, 1, 1, null, "fatal");
        o.SkipRemaining(new[] { Span(1, 3), Span(2, 5) });

        var r = o.Build(0);

        Assert.Equal(ExecuteStatus.Error, r.Status);
        Assert.Equal(new[] { ExecuteStatus.Error, ExecuteStatus.Skipped, ExecuteStatus.Skipped }, r.Batches.Select(b => b.Status));
        Assert.Equal(new[] { 0, 1, 2 }, r.Batches.Select(b => b.Index));
        Assert.Equal("The remaining 2 batch(es) did not run because the connection was closed.", r.Messages.Last());
    }

    [Fact]
    public void A_refused_script_is_an_Error_with_one_message_and_no_batch()
    {
        var o = new ExecuteOutcome();
        o.RefuseScript(ExecuteOutcome.SeparatorErrorText);

        var r = o.Build(0);

        Assert.Equal(ExecuteStatus.Error, r.Status);
        Assert.Empty(r.Batches);
        Assert.Empty(r.ResultSets);
        Assert.Equal(ExecuteOutcome.SeparatorErrorText, r.ErrorMessage);
        Assert.Equal(ExecuteOutcome.SeparatorErrorText, r.Messages.Single());
    }

    [Fact]
    public void Messages_are_charged_to_the_shared_budget_twice()
    {
        var o = new ExecuteOutcome(byteBudget: 10_000);
        o.BeginBatch(Span(0), 1);
        long before = o.BudgetRemaining;

        o.AddServerMessage(0, 0, 1, 1, null, new string('x', 1_000));

        Assert.True(before - o.BudgetRemaining >= 2_000);
    }

    [Fact]
    public void Info_messages_past_the_budget_are_omitted_but_errors_are_kept()
    {
        var o = new ExecuteOutcome(byteBudget: 3_000);
        o.BeginBatch(Span(0), 1);

        o.AddServerMessage(0, 0, 1, 1, null, new string('x', 2_000));   // over the budget: dropped
        o.AddServerMessage(208, 16, 1, 1, null, "still reported");

        var r = o.Build(0);
        Assert.Equal(1, r.MessagesOmitted);
        Assert.Contains(r.MessageDetails, m => m.Text == "still reported");
    }

    [Fact]
    public void A_set_reports_its_batch()
    {
        var o = new ExecuteOutcome();
        o.BeginBatch(Span(0), 1);
        o.EndBatch(0);
        o.BeginBatch(Span(1), 1);
        var set = Set();

        o.BeginSet(set);

        Assert.Equal(1, set.BatchIndex);
    }
}
