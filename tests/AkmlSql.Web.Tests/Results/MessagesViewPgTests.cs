using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Results;

/// <summary>
/// Spec 042 (task T049, FR-012 to FR-015): a PostgreSQL run in the Messages tab - the error as "SQLSTATE ...: message" with
/// its detail and hint, a click that goes to the line AND column, notices drawn quieter than errors, the tags as the server
/// words them, and the statements-ran and still-applied lines after a stopped run.
/// </summary>
public sealed class MessagesViewPgTests : BunitContext
{
    private static ExecuteMessageDto Error(string sqlState, string text, int line, int column, string? detail = null, string? hint = null) => new()
    {
        Kind = ExecuteMessageKind.Error, SqlState = sqlState, Text = text, Severity = 16, Line = line, Column = column, Detail = detail, Hint = hint,
    };

    private static ExecuteQueryResult Run(params ExecuteMessageDto[] messages) => new()
    {
        Status = messages.Any(m => m.Kind == ExecuteMessageKind.Error) ? ExecuteStatus.Error : ExecuteStatus.Ok,
        MessageDetails = messages,
        Batches = Array.Empty<ExecuteBatchDto>(),
    };

    [Fact]
    public void An_error_has_a_SQLSTATE_header_then_detail_and_hint_lines()
    {
        var result = Run(Error("23505", "duplicate key value violates unique constraint \"t_pkey\"", 4, 1,
            detail: "Key (id)=(1) already exists.", hint: "Use a different key."));

        var cut = Render<MessagesView>(p => p.Add(c => c.Result, result));

        var text = cut.Find("[data-testid=messages-error-0]").TextContent;
        Assert.Equal(
            "SQLSTATE 23505: duplicate key value violates unique constraint \"t_pkey\"\nDETAIL: Key (id)=(1) already exists.\nHINT: Use a different key.",
            text);
    }

    [Fact]
    public void An_error_with_a_position_is_a_button_that_names_its_line_and_column()
    {
        var result = Run(Error("42P01", "relation \"nope\" does not exist", 2, 15));
        DocumentPosition? clicked = null;

        var cut = Render<MessagesView>(p => p
            .Add(c => c.Result, result)
            .Add(c => c.OnNavigateToPosition, position => clicked = position));
        var button = cut.Find("button[data-testid=messages-error-0]");
        button.Click();

        Assert.Equal("Go to line 2, column 15", button.GetAttribute("title"));
        Assert.Equal(new DocumentPosition(2, 15), clicked);
    }

    [Fact]
    public void A_selection_that_started_later_in_the_document_moves_the_line_and_only_the_first_lines_column()
    {
        var onFirstLine = Error("42601", "syntax error", 1, 3);
        var later = Error("42601", "syntax error", 2, 7);
        var result = Run(onFirstLine, later);
        var clicks = new List<DocumentPosition>();

        var cut = Render<MessagesView>(p => p
            .Add(c => c.Result, result)
            .Add(c => c.SelectionStartLine, 10)
            .Add(c => c.SelectionStartColumn, 5)
            .Add(c => c.OnNavigateToPosition, position => clicks.Add(position)));
        cut.Find("button[data-testid=messages-error-0]").Click();
        cut.Find("button[data-testid=messages-error-1]").Click();

        // Line 1 of the selection is line 10 of the document, and its column starts at the selection's column 5.
        Assert.Equal(new[] { new DocumentPosition(10, 7), new DocumentPosition(11, 7) }, clicks);
    }

    [Fact]
    public void Without_a_position_listener_the_click_falls_back_to_the_line()
    {
        var result = Run(Error("42601", "syntax error", 3, 9));
        int? line = null;

        var cut = Render<MessagesView>(p => p.Add(c => c.Result, result).Add(c => c.OnNavigate, l => line = l));
        cut.Find("button[data-testid=messages-error-0]").Click();

        Assert.Equal(3, line);
    }

    [Fact]
    public void Notices_and_warnings_are_not_errors_and_carry_their_level_in_the_text()
    {
        var notice = new ExecuteMessageDto { Kind = ExecuteMessageKind.Info, Text = "NOTICE: hello 42", SqlState = "00000", Severity = 10 };
        var warning = new ExecuteMessageDto
        {
            Kind = ExecuteMessageKind.Info, Text = "WARNING: careful", SqlState = "01000", Severity = 10, Detail = "It was already there.", Hint = "Drop it first.",
        };
        var result = Run(notice, warning);

        var cut = Render<MessagesView>(p => p.Add(c => c.Result, result));

        var first = cut.Find("[data-testid=messages-line-0]");
        var second = cut.Find("[data-testid=messages-line-1]");
        Assert.Equal("NOTICE: hello 42", first.TextContent.Trim());
        Assert.Contains("akml-messages-line--notice", first.ClassName);
        Assert.DoesNotContain("akml-messages-line--error", first.ClassName);
        Assert.DoesNotContain("akml-messages-line--warning", first.ClassName);
        Assert.Contains("akml-messages-line--warning", second.ClassName);
        Assert.Equal("WARNING: careful\nDETAIL: It was already there.\nHINT: Drop it first.", second.TextContent.Trim());
        Assert.Empty(cut.FindAll("button.akml-messages-error"));
    }

    [Fact]
    public void Command_tags_read_as_the_server_words_them()
    {
        var tag = new ExecuteMessageDto { Kind = ExecuteMessageKind.RowCount, Text = "UPDATE 12", RowCount = 12 };
        var ddl = new ExecuteMessageDto { Kind = ExecuteMessageKind.RowCount, Text = "CREATE TABLE", RowCount = -1 };

        var cut = Render<MessagesView>(p => p.Add(c => c.Result, Run(tag, ddl)));

        Assert.Equal("UPDATE 12", cut.Find("[data-testid=messages-line-0]").TextContent.Trim());
        Assert.Equal("CREATE TABLE", cut.Find("[data-testid=messages-line-1]").TextContent.Trim());
    }

    [Fact]
    public void A_stopped_run_says_how_many_statements_ran_and_that_they_stay_applied()
    {
        var result = Run(
            new ExecuteMessageDto { Kind = ExecuteMessageKind.RowCount, Text = "INSERT 0 1", RowCount = 1 },
            Error("23505", "duplicate key", 2, 1),
            new ExecuteMessageDto { Kind = ExecuteMessageKind.System, Text = "4 of 6 statements ran." },
            new ExecuteMessageDto { Kind = ExecuteMessageKind.System, Text = "Earlier statements stay applied unless they ran inside an explicit transaction." },
            new ExecuteMessageDto { Kind = ExecuteMessageKind.System, Text = "Every statement fails until ROLLBACK." });

        var cut = Render<MessagesView>(p => p.Add(c => c.Result, result));

        var system = cut.FindAll(".akml-messages-line--system").Select(e => e.TextContent.Trim()).ToArray();
        Assert.Equal(new[]
        {
            "4 of 6 statements ran.",
            "Earlier statements stay applied unless they ran inside an explicit transaction.",
            "Every statement fails until ROLLBACK.",
        }, system);
    }

    [Fact]
    public void A_statement_that_was_not_sent_shows_why_and_is_still_an_error_line()
    {
        var skipped = new ExecuteMessageDto
        {
            Kind = ExecuteMessageKind.Error, Severity = 0, Line = 2, Column = 1,
            Text = "\\dt is a psql client command, not SQL, so it was not sent to the server. Use the Schema explorer to browse objects, or query the system catalogs.",
        };

        var cut = Render<MessagesView>(p => p.Add(c => c.Result, Run(skipped)));

        var line = cut.Find("[data-testid=messages-line-0]");
        Assert.Contains("psql client command", line.TextContent);
        Assert.Contains("akml-messages-line--error", line.ClassName);
    }

    [Fact]
    public void A_SQL_Server_error_is_unchanged()
    {
        var result = Run(new ExecuteMessageDto
        {
            Kind = ExecuteMessageKind.Error, Number = 208, Severity = 16, State = 1, Line = 4, Text = "Invalid object name 'dbo.Nope'.", BatchIndex = 0,
        });
        int? line = null;

        var cut = Render<MessagesView>(p => p.Add(c => c.Result, result).Add(c => c.OnNavigate, l => line = l));
        var button = cut.Find("button[data-testid=messages-error-0]");
        button.Click();

        Assert.Equal("Msg 208, Level 16, State 1, Line 4\nInvalid object name 'dbo.Nope'.", button.TextContent);
        Assert.Equal("Go to line 4", button.GetAttribute("title"));
        Assert.Equal(4, line);
    }
}

/// <summary>Spec 042 (task T049, FR-014): where a PostgreSQL error points, and that SQL Server's mapping is untouched.</summary>
public sealed class ExecuteLineMapperPgTests
{
    private static ExecuteQueryResult Result(params ExecuteBatchDto[] batches) => new() { Batches = batches };

    [Fact]
    public void A_PostgreSQL_line_is_already_within_the_script_and_adds_no_batch_offset()
    {
        // Three statements; the error is in the third, which starts on line 7 - but its Line already says 7.
        var result = Result(
            new ExecuteBatchDto { Index = 0, StartLine = 1 }, new ExecuteBatchDto { Index = 1, StartLine = 4 }, new ExecuteBatchDto { Index = 2, StartLine = 7 });
        var error = new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, SqlState = "42601", Line = 7, Column = 3, BatchIndex = 2 };

        Assert.Equal(7, ExecuteLineMapper.DocumentLine(1, result, error));
        Assert.Equal(16, ExecuteLineMapper.DocumentLine(10, result, error));
    }

    [Fact]
    public void The_column_moves_only_on_the_first_line_of_a_selection()
    {
        var first = new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, SqlState = "42601", Line = 1, Column = 3 };
        var later = new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, SqlState = "42601", Line = 2, Column = 3 };

        Assert.Equal(7, ExecuteLineMapper.DocumentColumn(5, first));
        Assert.Equal(3, ExecuteLineMapper.DocumentColumn(5, later));
        Assert.Equal(3, ExecuteLineMapper.DocumentColumn(1, first));
    }

    [Fact]
    public void An_error_without_a_position_points_nowhere()
    {
        var noLine = new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, SqlState = "57014", Line = 0 };
        var notAnError = new ExecuteMessageDto { Kind = ExecuteMessageKind.Info, SqlState = "00000", Line = 3, Column = 1 };

        Assert.Null(ExecuteLineMapper.DocumentLine(1, Result(), noLine));
        Assert.Null(ExecuteLineMapper.DocumentLine(1, Result(), notAnError));
        Assert.Null(ExecuteLineMapper.DocumentColumn(1, noLine));
        Assert.Null(ExecuteLineMapper.DocumentColumn(1, notAnError));
    }

    [Fact]
    public void SQL_Server_errors_still_map_through_their_batch_and_have_no_column()
    {
        var result = Result(new ExecuteBatchDto { Index = 0, StartLine = 1 }, new ExecuteBatchDto { Index = 1, StartLine = 5 });
        var error = new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, Number = 208, Severity = 16, Line = 2, BatchIndex = 1 };
        var inProcedure = new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, Number = 50000, Severity = 16, Line = 2, BatchIndex = 1, Procedure = "dbo.p" };

        Assert.Equal(6, ExecuteLineMapper.DocumentLine(1, result, error));
        Assert.Null(ExecuteLineMapper.DocumentLine(1, result, inProcedure));
        Assert.Null(ExecuteLineMapper.DocumentColumn(1, error));
    }
}
