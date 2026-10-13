using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;
using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Results;

/// <summary>
/// Spec 041 (T084, research R20/R24, FR-022, FR-023, FR-035) — the Messages tab: every message as SSMS words
/// it, in order; an error that maps to a document line is clickable; the completion time closes the list.
/// </summary>
public sealed class MessagesViewTests : IDisposable
{
    private readonly BunitContext _ctx = new();

    public MessagesViewTests() => _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

    public void Dispose() => _ctx.Dispose();

    private static readonly DateTimeOffset Completed = new(2026, 10, 8, 10, 34, 56, TimeSpan.Zero);

    private IRenderedComponent<MessagesView> Render(ExecuteQueryResult result, int selectionStart = 1, Action<int>? navigate = null) =>
        _ctx.Render<MessagesView>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.SelectionStartLine, selectionStart)
            .Add(x => x.ReceivedAt, Completed.AddMinutes(5))
            .Add(x => x.OnNavigate, (int line) => navigate?.Invoke(line)));

    private static ExecuteQueryResult Script() => new()
    {
        Status = ExecuteStatus.Error,
        CompletedAtUnixMs = Completed.ToUnixTimeMilliseconds(),
        Batches = new[] { new ExecuteBatchDto { Index = 0, StartLine = 1 }, new ExecuteBatchDto { Index = 1, StartLine = 5 } },
        MessageDetails = new[]
        {
            new ExecuteMessageDto { Kind = ExecuteMessageKind.RowCount, RowCount = 2, Text = "(2 rows affected)" },
            new ExecuteMessageDto { Kind = ExecuteMessageKind.Info, Text = "printed" },
            new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, BatchIndex = 1, Number = 208, Severity = 16, State = 1, Line = 2, Text = "Invalid object name 'nope'." },
            new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, BatchIndex = 1, Number = 50000, Severity = 16, State = 1, Line = 3, Procedure = "dbo.p", Text = "boom" },
        },
    };

    [Fact]
    public void Every_message_is_a_line_in_order_worded_as_in_SSMS()
    {
        var cut = Render(Script());

        Assert.Equal("(2 rows affected)", cut.Find("[data-testid=messages-line-0]").TextContent.Trim());
        Assert.Equal("printed", cut.Find("[data-testid=messages-line-1]").TextContent.Trim());
        Assert.Equal("Msg 208, Level 16, State 1, Line 2\nInvalid object name 'nope'.", cut.Find("[data-testid=messages-line-2]").TextContent.Trim());
        Assert.NotNull(cut.Find("[data-testid=messages-list]"));
    }

    [Fact]
    public void A_mappable_error_navigates_to_its_document_line()
    {
        int? line = null;
        var cut = Render(Script(), selectionStart: 10, navigate: l => line = l);

        cut.Find("[data-testid=messages-error-2]").Click();

        Assert.Equal((10 - 1) + (5 - 1) + 2, line);
    }

    [Fact]
    public void An_error_in_a_procedure_is_shown_but_not_clickable()
    {
        var cut = Render(Script());

        var error = cut.Find("[data-testid=messages-error-3]");
        Assert.NotEqual("BUTTON", error.TagName);
        Assert.Contains("Procedure dbo.p", error.TextContent);
    }

    [Fact]
    public void The_last_line_is_the_completion_time_in_local_time()
    {
        var cut = Render(Script());

        var expected = SqlMessageText.CompletionLine(Completed.ToLocalTime());
        Assert.Equal(expected, cut.Find("[data-testid=messages-completion]").TextContent.Trim());
    }

    [Fact]
    public void Without_the_engines_completion_time_the_receipt_time_is_used()
    {
        var result = Script();
        result.CompletedAtUnixMs = 0;

        var cut = Render(result);

        Assert.Equal(SqlMessageText.CompletionLine(Completed.AddMinutes(5).ToLocalTime()),
            cut.Find("[data-testid=messages-completion]").TextContent.Trim());
    }

    [Fact]
    public void An_older_engines_message_strings_are_shown()
    {
        var older = new ExecuteQueryResult { Status = ExecuteStatus.Ok, Messages = new[] { "(1 row affected)", "hello" } };

        var cut = Render(older);

        Assert.Equal("(1 row affected)", cut.Find("[data-testid=messages-line-0]").TextContent.Trim());
        Assert.Equal("hello", cut.Find("[data-testid=messages-line-1]").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid^=messages-error-]"));
    }

    [Fact]
    public void The_omitted_messages_summary_is_shown()
    {
        var result = new ExecuteQueryResult
        {
            Status = ExecuteStatus.Ok,
            MessagesOmitted = 2_000,
            MessageDetails = new[] { new ExecuteMessageDto { Kind = ExecuteMessageKind.System, Text = "2,000 more message(s) were not returned (the limit is 10,000)." } },
        };

        var cut = Render(result);

        Assert.Contains("2,000 more message(s)", cut.Find("[data-testid=messages-line-0]").TextContent);
    }

    [Fact]
    public void A_script_with_nothing_to_say_reads_commands_completed_successfully()
    {
        var ddl = new ExecuteQueryResult { Status = ExecuteStatus.Ok, CompletedAtUnixMs = Completed.ToUnixTimeMilliseconds() };

        var cut = Render(ddl);

        Assert.Equal(SqlMessageText.CommandsCompleted, cut.Find("[data-testid=messages-line-0]").TextContent.Trim());
        Assert.NotNull(cut.Find("[data-testid=messages-completion]"));
    }

    [Fact]
    public void A_failed_run_without_messages_shows_its_error_instead()
    {
        var failed = new ExecuteQueryResult { Status = ExecuteStatus.NoReply, ErrorMessage = "No result was received within the timeout." };

        var cut = Render(failed);

        Assert.Contains("No result was received", cut.Find("[data-testid=messages-line-0]").TextContent);
        Assert.DoesNotContain(SqlMessageText.CommandsCompleted, cut.Markup);
    }
}
