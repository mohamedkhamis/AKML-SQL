using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// Spec 042 (FR-012 to FR-015, research R7, tasks T058): the PostgreSQL runner reuses <see cref="ExecuteOutcome"/> for the
/// caps and the reply, with one batch record per statement. These pin the two additions: a command-tag line that is also
/// the statement's row count, and a message that carries SQLSTATE, detail, hint and the error position.
/// </summary>
public class ExecuteOutcomePgTests
{
    private static SqlBatchSpan Statement(int index, string text) => new(index, 0, text.Length, 1 + index, 1, text);

    [Fact]
    public void A_command_tag_is_a_row_count_line_with_the_servers_text()
    {
        var outcome = new ExecuteOutcome();
        outcome.BeginBatch(Statement(0, "UPDATE t SET a = 1"), 1);

        outcome.AddCommandTag("UPDATE 12", 12);
        outcome.EndBatch(5);
        var reply = outcome.Build(1);

        var message = Assert.Single(reply.MessageDetails);
        Assert.Equal(ExecuteMessageKind.RowCount, message.Kind);
        Assert.Equal("UPDATE 12", message.Text);
        Assert.Equal(12, message.RowCount);
        Assert.Equal(12, reply.TotalRowsAffected);
        Assert.Equal("UPDATE 12", reply.Messages[0]);
    }

    [Fact]
    public void A_commands_tag_without_rows_counts_nothing()
    {
        var outcome = new ExecuteOutcome();
        outcome.BeginBatch(Statement(0, "CREATE TABLE t (a int)"), 1);

        outcome.AddCommandTag("CREATE TABLE", -1);
        var reply = outcome.Build(1);

        Assert.Equal("CREATE TABLE", Assert.Single(reply.MessageDetails).Text);
        Assert.Equal(-1, reply.TotalRowsAffected);
    }

    [Fact]
    public void A_SELECTs_tag_belongs_to_its_result_set_and_is_not_DML()
    {
        var outcome = new ExecuteOutcome();
        outcome.BeginBatch(Statement(0, "SELECT 1"), 1);
        var index = outcome.BeginSet(new ExecuteResultSet { ColumnNames = ["n"], Rows = [["1"]] });

        outcome.AddCommandTag("SELECT 1", 1);
        var reply = outcome.Build(1);

        var message = Assert.Single(reply.MessageDetails);
        Assert.Equal(index, message.ResultSetIndex);
        Assert.Equal(-1, reply.TotalRowsAffected);
    }

    [Fact]
    public void A_PostgreSQL_error_keeps_sqlstate_detail_hint_line_and_column_and_marks_the_statement()
    {
        var outcome = new ExecuteOutcome();
        outcome.BeginBatch(Statement(0, "SELECT * FROM nope"), 1);

        outcome.AddPostgresMessage(ExecuteMessageKind.Error, "relation \"nope\" does not exist", severity: 16,
            sqlState: "42P01", detail: "d", hint: "h", line: 4, column: 15);
        outcome.EndBatch(2);
        var reply = outcome.Build(1);

        var message = Assert.Single(reply.MessageDetails);
        Assert.Equal("42P01", message.SqlState);
        Assert.Equal("d", message.Detail);
        Assert.Equal("h", message.Hint);
        Assert.Equal(4, message.Line);
        Assert.Equal(15, message.Column);
        Assert.Equal(0, message.Number);
        Assert.Equal(ExecuteStatus.Error, reply.Status);
        Assert.Equal("relation \"nope\" does not exist", reply.ErrorMessage);
        var batch = Assert.Single(reply.Batches);
        Assert.Equal(ExecuteStatus.Error, batch.Status);
        Assert.Equal(1, batch.ErrorCount);
        Assert.Contains("SQLSTATE 42P01: relation \"nope\" does not exist", reply.Messages[0]);
    }

    [Fact]
    public void A_notice_is_information_not_an_error()
    {
        var outcome = new ExecuteOutcome();
        outcome.BeginBatch(Statement(0, "DO $$ BEGIN RAISE NOTICE 'x'; END $$"), 1);

        outcome.AddPostgresMessage(ExecuteMessageKind.Info, "NOTICE:  x", severity: 10);
        var reply = outcome.Build(1);

        Assert.Equal(ExecuteStatus.Ok, reply.Status);
        Assert.Equal(ExecuteMessageKind.Info, Assert.Single(reply.MessageDetails).Kind);
        Assert.Equal(ExecuteStatus.Ok, Assert.Single(reply.Batches).Status);
    }

    [Fact]
    public void Statements_after_a_failure_are_listed_as_skipped_without_a_connection_closed_line()
    {
        var outcome = new ExecuteOutcome();
        outcome.BeginBatch(Statement(0, "SELECT 1/0"), 1);
        outcome.AddPostgresMessage(ExecuteMessageKind.Error, "division by zero", 16, "22012");
        outcome.EndBatch(1);

        outcome.SkipAfterStop([Statement(1, "SELECT 2"), Statement(2, "SELECT 3")]);
        var reply = outcome.Build(1);

        Assert.Equal(new[] { ExecuteStatus.Error, ExecuteStatus.Skipped, ExecuteStatus.Skipped }, reply.Batches.Select(b => b.Status).ToArray());
        Assert.DoesNotContain(reply.MessageDetails, m => m.Text.Contains("connection was closed"));
    }

    [Fact]
    public void The_existing_SQL_Server_accounting_is_unchanged()
    {
        var outcome = new ExecuteOutcome();
        outcome.BeginBatch(Statement(0, "SELECT 1"), 1);
        outcome.AddServerMessage(208, 16, 1, 3, null, "Invalid object name 'x'.");
        outcome.AddRowCount(3);

        var reply = outcome.Build(1);

        Assert.Equal(ExecuteStatus.Error, reply.Status);
        Assert.Equal(3, reply.TotalRowsAffected);
        Assert.Null(reply.MessageDetails[0].SqlState);
    }
}
