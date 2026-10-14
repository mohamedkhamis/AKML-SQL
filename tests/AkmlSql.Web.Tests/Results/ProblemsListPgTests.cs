using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Results;

/// <summary>
/// Spec 042 (task T049, FR-014): the Problems tab lists the errors of the last PostgreSQL run, each at its line and column,
/// whatever the analysis switch says; clicking one goes there; SQL Server errors, which the tab never listed, still are not.
/// </summary>
public sealed class ProblemsListPgTests : BunitContext
{
    private static ExecuteMessageDto PgError(string sqlState, string text, int line, int column) => new()
    {
        Kind = ExecuteMessageKind.Error, SqlState = sqlState, Text = text, Severity = 16, Line = line, Column = column,
    };

    private static ExecuteQueryResult Run(params ExecuteMessageDto[] messages) => new() { Status = ExecuteStatus.Error, MessageDetails = messages };

    private static CodeAnalysisResponse Findings(params CodeIssueInfo[] issues) => new() { Issues = issues };

    [Fact]
    public void Each_error_of_the_run_is_a_row_with_its_code_message_and_position()
    {
        var run = Run(PgError("42P01", "relation \"nope\" does not exist", 2, 15));

        var cut = Render<ProblemsListComponent>(p => p.Add(c => c.ExecutionResult, run));

        var row = cut.Find("[data-testid=problem-execution]");
        Assert.Equal("2", row.GetAttribute("data-line"));
        Assert.Equal("15", row.GetAttribute("data-column"));
        Assert.Contains("42P01", row.TextContent);
        Assert.Contains("relation \"nope\" does not exist", row.TextContent);
        Assert.Contains("L2:15", row.TextContent);
        Assert.Contains("SQLSTATE 42P01", row.GetAttribute("title"));
    }

    [Fact]
    public void Clicking_a_row_goes_to_the_documents_line_and_column()
    {
        var run = Run(PgError("42601", "syntax error", 1, 3));
        DocumentPosition? target = null;

        var cut = Render<ProblemsListComponent>(p => p
            .Add(c => c.ExecutionResult, run)
            .Add(c => c.SelectionStartLine, 10)
            .Add(c => c.SelectionStartColumn, 5)
            .Add(c => c.OnJumpToPosition, position => target = position));
        cut.Find("[data-testid=problem-execution]").Click();

        Assert.Equal(new DocumentPosition(10, 7), target);
    }

    [Fact]
    public void They_show_with_analysis_off_and_are_counted_in_the_badge()
    {
        var run = Run(PgError("42601", "syntax error", 1, 1), PgError("42703", "column \"x\" does not exist", 3, 8));
        var counts = new List<int>();

        var cut = Render<ProblemsListComponent>(p => p
            .Add(c => c.ExecutionResult, run)
            .Add(c => c.AnalysisEnabled, false)
            .Add(c => c.OnVisibleCountChanged, n => counts.Add(n)));

        Assert.Equal(2, cut.FindAll("[data-testid=problem-execution]").Count);
        Assert.NotEmpty(cut.FindAll("[data-testid=problems-analysis-off]"));
        Assert.Equal(2, counts.Last());
    }

    [Fact]
    public void They_sit_above_the_analysers_findings_and_the_empty_text_is_not_shown_for_them()
    {
        var run = Run(PgError("42601", "syntax error", 1, 1));
        var issue = new CodeIssueInfo { RuleId = "PGBP001", Message = "Avoid SELECT *", Line = 1, Column = 1, Severity = 2 };

        var onlyErrors = Render<ProblemsListComponent>(p => p
            .Add(c => c.ExecutionResult, run)
            .Add(c => c.Findings, Findings()));
        Assert.Empty(onlyErrors.FindAll("[data-testid=problems-empty]"));

        var both = Render<ProblemsListComponent>(p => p
            .Add(c => c.ExecutionResult, run)
            .Add(c => c.Findings, Findings(issue)));
        Assert.Single(both.FindAll("[data-testid=problem-execution]"));
        Assert.Single(both.FindAll("[data-testid=problem-item]"));
        var html = both.Markup;
        Assert.True(html.IndexOf("problem-execution", StringComparison.Ordinal) < html.IndexOf("problem-item", StringComparison.Ordinal));
    }

    [Fact]
    public void SQL_Server_errors_and_notices_are_not_listed()
    {
        var sqlServer = new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, Number = 208, Severity = 16, Line = 2, Text = "Invalid object name" };
        var notice = new ExecuteMessageDto { Kind = ExecuteMessageKind.Info, SqlState = "00000", Text = "NOTICE: hi" };

        var cut = Render<ProblemsListComponent>(p => p.Add(c => c.ExecutionResult, Run(sqlServer, notice)));

        Assert.Empty(cut.FindAll("[data-testid=problem-execution]"));
    }
}
