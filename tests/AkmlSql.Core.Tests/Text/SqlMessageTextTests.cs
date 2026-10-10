using System;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;
using Xunit;

namespace AkmlSql.Core.Tests.Text;

/// <summary>
/// Spec 041 (T077, research R18, FR-022) — the Messages tab's lines, worded exactly as SSMS words them.
/// The engine fills the legacy <c>Messages[]</c> strings with these and the web renders the structured
/// messages through them, so both editions read the same.
/// </summary>
public class SqlMessageTextTests
{
    [Fact]
    public void An_error_has_the_SSMS_header_then_the_text_on_the_next_line()
    {
        Assert.Equal("Msg 208, Level 16, State 1, Line 4\nInvalid object name 'dbo.Nope'.",
            SqlMessageText.Error(208, 16, 1, 4, null, "Invalid object name 'dbo.Nope'."));
    }

    [Fact]
    public void An_error_raised_in_a_procedure_names_it_before_the_line()
    {
        Assert.Equal("Msg 50000, Level 16, State 1, Procedure dbo.p, Line 3\nboom",
            SqlMessageText.Error(50000, 16, 1, 3, "dbo.p", "boom"));
    }

    [Fact]
    public void An_empty_procedure_name_is_left_out()
    {
        Assert.Equal("Msg 1, Level 16, State 1, Line 1\nx", SqlMessageText.Error(1, 16, 1, 1, "", "x"));
    }

    [Theory]
    [InlineData(1, false, "(1 row affected)")]
    [InlineData(0, false, "(0 rows affected)")]
    [InlineData(1234, false, "(1234 rows affected)")]
    [InlineData(1000, true, "(1000 rows returned; more exist)")]
    [InlineData(1, true, "(1 row returned; more exist)")]
    public void Row_counts_read_as_in_SSMS(long count, bool moreExist, string expected)
    {
        Assert.Equal(expected, SqlMessageText.RowCount(count, moreExist));
    }

    [Fact]
    public void Info_and_system_lines_are_the_text_itself()
    {
        Assert.Equal("hello from PRINT", SqlMessageText.Info("hello from PRINT"));
        Assert.Equal("Beginning execution loop", SqlMessageText.System("Beginning execution loop"));
    }

    [Fact]
    public void The_completion_line_is_local_ISO_time_with_seven_digits_and_the_offset()
    {
        var local = new DateTimeOffset(2026, 10, 8, 12, 34, 56, TimeSpan.FromHours(2)).AddTicks(1234567);

        Assert.Equal("Completion time: 2026-10-08T12:34:56.1234567+02:00", SqlMessageText.CompletionLine(local));
    }

    [Fact]
    public void The_commands_completed_line()
    {
        Assert.Equal("Commands completed successfully.", SqlMessageText.CommandsCompleted);
    }

    [Fact]
    public void A_message_dto_is_worded_by_its_kind()
    {
        Assert.Equal("Msg 8134, Level 16, State 1, Line 2\nDivide by zero error encountered.",
            SqlMessageText.Format(new ExecuteMessageDto
            {
                Kind = ExecuteMessageKind.Error, Number = 8134, Severity = 16, State = 1, Line = 2,
                Text = "Divide by zero error encountered.",
            }));
        Assert.Equal("(3 rows affected)", SqlMessageText.Format(new ExecuteMessageDto { Kind = ExecuteMessageKind.RowCount, Text = "(3 rows affected)", RowCount = 3 }));
        Assert.Equal("printed", SqlMessageText.Format(new ExecuteMessageDto { Kind = ExecuteMessageKind.Info, Text = "printed" }));
        Assert.Equal("Batch execution completed 3 times.", SqlMessageText.Format(new ExecuteMessageDto { Kind = ExecuteMessageKind.System, Text = "Batch execution completed 3 times." }));
    }

    [Fact]
    public void An_engine_raised_error_without_a_server_number_is_its_text()
    {
        Assert.Equal("A fatal scripting error occurred.\nIncorrect syntax was encountered while parsing GO.",
            SqlMessageText.Format(new ExecuteMessageDto
            {
                Kind = ExecuteMessageKind.Error,
                Text = "A fatal scripting error occurred.\nIncorrect syntax was encountered while parsing GO.",
            }));
    }

    [Fact]
    public void A_row_count_dto_without_text_is_worded_from_its_count()
    {
        Assert.Equal("(7 rows affected)", SqlMessageText.Format(new ExecuteMessageDto { Kind = ExecuteMessageKind.RowCount, RowCount = 7 }));
    }
}
