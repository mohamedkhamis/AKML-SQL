using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;
using Xunit;

namespace AkmlSql.Core.Tests.Text;

/// <summary>
/// Spec 042 (FR-013, FR-014, task T046): a PostgreSQL error reads as its SQLSTATE and message, then the server's DETAIL and
/// HINT lines; notices carry their level in the text and read as written. SQL Server messages are worded exactly as before.
/// </summary>
public class SqlMessageTextPgTests
{
    private static ExecuteMessageDto PgError(string text, string sqlState = "42P01", string? detail = null, string? hint = null, int line = 3) => new()
    {
        Kind = ExecuteMessageKind.Error,
        Text = text,
        Severity = 16,
        SqlState = sqlState,
        Detail = detail,
        Hint = hint,
        Line = line,
        Column = 14,
    };

    [Fact]
    public void An_error_reads_as_SQLSTATE_then_message()
    {
        var text = SqlMessageText.Format(PgError("relation \"no_such_table\" does not exist"));

        Assert.StartsWith("SQLSTATE 42P01: relation \"no_such_table\" does not exist", text);
    }

    [Fact]
    public void Detail_and_hint_follow_on_their_own_lines()
    {
        var text = SqlMessageText.Format(PgError("duplicate key value violates unique constraint \"pk\"", "23505",
            detail: "Key (id)=(1) already exists.", hint: "Use a different id."));

        Assert.Equal(
            "SQLSTATE 23505: duplicate key value violates unique constraint \"pk\"\nDETAIL: Key (id)=(1) already exists.\nHINT: Use a different id.",
            text);
    }

    [Fact]
    public void Empty_detail_and_hint_are_left_out()
    {
        var text = SqlMessageText.Format(PgError("syntax error at or near \"SELEC\"", "42601", detail: "", hint: null));

        Assert.DoesNotContain("DETAIL", text);
        Assert.DoesNotContain("HINT", text);
    }

    [Fact]
    public void The_SQL_Server_header_is_not_used_for_a_PostgreSQL_error()
    {
        var text = SqlMessageText.Format(PgError("boom"));

        Assert.DoesNotContain("Msg ", text);
        Assert.DoesNotContain("Level", text);
    }

    [Fact]
    public void Notices_and_command_tags_read_as_written()
    {
        var notice = new ExecuteMessageDto { Kind = ExecuteMessageKind.Info, Text = "NOTICE:  hello from plpgsql", Severity = 10 };
        var tag = new ExecuteMessageDto { Kind = ExecuteMessageKind.RowCount, Text = "UPDATE 12", RowCount = 12 };

        Assert.Equal("NOTICE:  hello from plpgsql", SqlMessageText.Format(notice));
        Assert.Equal("UPDATE 12", SqlMessageText.Format(tag));
    }

    [Fact]
    public void A_SQL_Server_error_is_worded_exactly_as_before()
    {
        var message = new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, Number = 208, Severity = 16, State = 1, Line = 4, Text = "Invalid object name 'x'." };

        Assert.Equal("Msg 208, Level 16, State 1, Line 4\nInvalid object name 'x'.", SqlMessageText.Format(message));
    }

    [Fact]
    public void ForPostgres_can_be_called_directly_for_the_web_problems_list()
    {
        Assert.Equal("SQLSTATE 42601: bad", SqlMessageText.ForPostgres(PgError("bad", "42601", line: 1)));
    }
}
