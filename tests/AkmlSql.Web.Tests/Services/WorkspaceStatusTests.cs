using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 041 (T026, research R41, X8) — the one place the editor page reports what is happening: the
/// execution phase and its outcome, rows and time, the caret, and the document's name and modified
/// state. The results pane's running indicator and the status bar both read it.
/// </summary>
public sealed class WorkspaceStatusTests
{
    private DateTimeOffset _now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly WorkspaceStatus _status;
    private int _changes;

    public WorkspaceStatusTests()
    {
        _status = new WorkspaceStatus(() => _now);
        _status.Changed += () => _changes++;
    }

    private static ExecuteResultSet Set(int rows) => new()
    {
        ColumnNames = new[] { "a" },
        Rows = Enumerable.Range(0, rows).Select(i => new string?[] { i.ToString() }).ToArray(),
    };

    [Fact]
    public void Starts_idle()
    {
        Assert.Equal(ExecutionPhase.Idle, _status.Phase);
        Assert.Null(_status.StartedAt);
        Assert.Null(_status.Outcome);
    }

    [Fact]
    public void Begin_enters_running_with_the_start_time()
    {
        _status.Begin();

        Assert.Equal(ExecutionPhase.Running, _status.Phase);
        Assert.Equal(_now, _status.StartedAt);
        Assert.Equal("Executing query…", _status.Outcome);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Elapsed_time_counts_up_while_running()
    {
        _status.Begin();
        _now = _now.AddMilliseconds(2_100);

        Assert.Equal(2_100, _status.ElapsedMs);
    }

    [Fact]
    public void An_ok_result_succeeds_with_rows_across_sets_and_the_engine_time()
    {
        _status.Begin();
        _status.Complete(new ExecuteQueryResult
        {
            Status = ExecuteStatus.Ok,
            ElapsedMs = 87,
            ResultSets = new[] { Set(10), Set(6) },
        });

        Assert.Equal(ExecutionPhase.Succeeded, _status.Phase);
        Assert.Equal("Query executed successfully.", _status.Outcome);
        Assert.Equal(16, _status.RowCount);
        Assert.Equal(87, _status.ElapsedMs);
        Assert.Null(_status.ErrorMessage);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void Without_a_result_set_the_row_count_is_the_rows_affected()
    {
        _status.Begin();
        _status.Complete(new ExecuteQueryResult { Status = ExecuteStatus.Ok, TotalRowsAffected = 3 });

        Assert.Equal(3, _status.RowCount);
    }

    [Fact]
    public void No_set_and_no_count_reads_zero_rows()
    {
        _status.Begin();
        _status.Complete(new ExecuteQueryResult { Status = ExecuteStatus.Ok, TotalRowsAffected = -1 });

        Assert.Equal(0, _status.RowCount);
    }

    [Theory]
    [InlineData(ExecuteStatus.Error)]
    [InlineData(ExecuteStatus.TimedOut)]
    [InlineData(ExecuteStatus.Cancelled)]
    [InlineData(ExecuteStatus.NoConnection)]
    public void Any_other_status_fails_with_its_message(int status)
    {
        _status.Begin();
        _status.Complete(new ExecuteQueryResult { Status = status, ErrorMessage = "Invalid object name 'x'." });

        Assert.Equal(ExecutionPhase.Failed, _status.Phase);
        Assert.Equal("Query completed with errors.", _status.Outcome);
        Assert.Equal("Invalid object name 'x'.", _status.ErrorMessage);
    }

    [Fact]
    public void Fail_records_a_message_without_a_result()
    {
        _status.Begin();
        _now = _now.AddMilliseconds(500);
        _status.Fail("The engine connection dropped; the statement may still have run.");

        Assert.Equal(ExecutionPhase.Failed, _status.Phase);
        Assert.Equal("The engine connection dropped; the statement may still have run.", _status.ErrorMessage);
        Assert.Equal(500, _status.ElapsedMs);   // frozen at the moment it failed
        _now = _now.AddSeconds(5);
        Assert.Equal(500, _status.ElapsedMs);
    }

    [Fact]
    public void A_new_run_clears_the_previous_outcome()
    {
        _status.Begin();
        _status.Fail("boom");
        _status.Begin();

        Assert.Equal(ExecutionPhase.Running, _status.Phase);
        Assert.Null(_status.ErrorMessage);
    }

    [Fact]
    public void SetCaret_updates_the_caret_and_raises_Changed_once_per_change()
    {
        _status.SetCaret(1, 12);
        Assert.Equal(1, _status.CaretLine);
        Assert.Equal(12, _status.CaretColumn);
        Assert.Equal(1, _changes);

        _status.SetCaret(1, 12);    // unchanged: no re-render
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void SetDocument_updates_the_name_and_modified_state()
    {
        _status.SetDocument("orders-audit", modified: true);

        Assert.Equal("orders-audit", _status.DocumentName);
        Assert.True(_status.IsModified);
        Assert.Equal(1, _changes);

        _status.SetDocument("orders-audit", modified: true);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void The_last_result_is_kept_for_the_next_visit_to_the_editor()
    {
        Assert.Null(_status.LastResult);
        var result = new ExecuteQueryResult { Status = ExecuteStatus.Ok, ResultSets = new[] { Set(2) } };

        _status.KeepResult(new WorkspaceResult(result, ConnectionGeneration: 3, StartLine: 4, StartColumn: 1, Sql: "SELECT 1"));

        Assert.Same(result, _status.LastResult!.Result);
        Assert.Equal(3, _status.LastResult.ConnectionGeneration);
        Assert.Equal("SELECT 1", _status.LastResult.Sql);
    }

    [Fact]
    public void A_connection_change_clears_the_outcome_but_not_a_run_in_flight()
    {
        _status.Begin();
        _status.Complete(new ExecuteQueryResult { Status = ExecuteStatus.Ok, ResultSets = new[] { Set(196) }, ElapsedMs = 28 });
        _changes = 0;

        _status.ClearOutcome();

        Assert.Equal(ExecutionPhase.Idle, _status.Phase);
        Assert.Null(_status.Outcome);
        Assert.Equal(0, _status.RowCount);
        Assert.Equal(0, _status.ElapsedMs);
        Assert.Equal(1, _changes);

        _status.Begin();
        _status.ClearOutcome();
        Assert.Equal(ExecutionPhase.Running, _status.Phase);
    }
}
