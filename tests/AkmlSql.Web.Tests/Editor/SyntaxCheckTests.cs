using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 047 (WEB-005, contracts/analysis-display.md §4) — Parse for a T-SQL tab needs no connection: the browser checks the
/// script when the server cannot, the Messages read like the server's, the first error gets the red mark, and the status
/// says "Parse failed: 1 syntax error." (a run keeps "Query completed with errors."). The page wires these decisions
/// (Pages/Editor.razor ParseAsync); the browser test ParseMarkerBrowserTests drives the whole path.
/// </summary>
public sealed class SyntaxCheckTests
{
    [Theory]
    [InlineData(false, false, false, false, true)]   // T-SQL, no connection: checked in the browser
    [InlineData(false, false, true, true, true)]     // T-SQL, connected: on the server
    [InlineData(true, false, true, true, false)]     // a run in progress
    [InlineData(false, true, false, false, false)]   // PostgreSQL needs its server
    [InlineData(false, true, true, false, false)]    //   and an engine that parses
    [InlineData(false, true, true, true, true)]
    public void Parse_is_available_for_TSQL_without_a_connection(bool running, bool postgres, bool connected, bool engineParses, bool available)
    {
        Assert.Equal(available, SyntaxCheck.CanParse(running, postgres, connected, engineParses));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]   // an engine from before execute.v2
    [InlineData(true, true, false, false)]   // the link is down
    public void The_server_checks_it_only_when_it_can(bool connected, bool engineParses, bool linkOpen, bool onServer)
    {
        Assert.Equal(onServer, SyntaxCheck.OnServer(connected, engineParses, linkOpen));
    }

    [Fact]
    public void A_browser_check_reads_like_the_servers_reply()
    {
        var result = SyntaxCheck.InBrowser("SELEC * FROM dbo.Customers;", "q1");

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Equal(SyntaxCheck.CheckedInBrowser, result.MessageDetails[0].Text);
        Assert.Equal(ExecuteMessageKind.Info, result.MessageDetails[0].Kind);
        var error = Assert.Single(result.MessageDetails, m => m.Kind == ExecuteMessageKind.Error);
        Assert.Equal(102, error.Number);
        Assert.Equal(15, error.Severity);
        Assert.Equal(1, error.Line);
        Assert.StartsWith("Msg 102, Level 15, State 1, Line 1", SqlMessageText.Format(error));
        Assert.Equal(WorkspaceStatus.ParseFailedOne, SyntaxCheck.Outcome(result));
    }

    [Fact]
    public void Several_errors_are_counted_and_valid_SQL_succeeds()
    {
        // ("SELEC 1" alone would parse: a batch may start with a procedure name, called without EXEC.)
        var two = SyntaxCheck.InBrowser("SELEC * FROM dbo.a;\nGO\nSELEC * FROM dbo.b;", "q2");
        Assert.Equal(string.Format(WorkspaceStatus.ParseFailedMany, 2), SyntaxCheck.Outcome(two));
        Assert.Equal("Parse failed: 2 syntax errors.", SyntaxCheck.Outcome(two));

        var valid = SyntaxCheck.InBrowser("SELECT 1;", "q3");
        Assert.Equal(ExecuteStatus.Ok, valid.Status);
        Assert.Equal("Parse succeeded.", SyntaxCheck.Outcome(valid));
    }

    [Fact]
    public void A_GO_repeat_count_is_not_a_syntax_error()
    {
        var result = SyntaxCheck.InBrowser("select 1\nGO 3\nprint 'x'\nGO 2 -- twice\n", "q4");

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Equal(WorkspaceStatus.ParseSucceeded, SyntaxCheck.Outcome(result));
    }

    [Fact]
    public void The_first_error_maps_to_its_document_line()
    {
        // A selection that starts on line 3: the parser's line 1 is the document's line 3.
        var result = SyntaxCheck.InBrowser("SELEC * FROM dbo.Customers;", "q5");

        var first = SyntaxCheck.FirstError(result, selectionStartLine: 3);

        Assert.NotNull(first);
        Assert.Equal(3, first!.Value.Line);
        Assert.Equal(ExecuteMessageKind.Error, first.Value.Message.Kind);
        Assert.Null(SyntaxCheck.FirstError(SyntaxCheck.InBrowser("SELECT 1;", "q6"), 1));
    }

    [Fact]
    public void The_status_shows_the_parse_words_until_the_next_run()
    {
        var status = new WorkspaceStatus();
        var parsed = RunSnapshot.Of(SyntaxCheck.InBrowser("SELEC * FROM dbo.a;", "q7")) with { Outcome = WorkspaceStatus.ParseFailedOne };

        status.Show(parsed);
        Assert.Equal("Parse failed: 1 syntax error.", status.Outcome);
        Assert.Equal(WorkspaceStatus.ParseFailedOne, status.Snapshot().Outcome);   // a tab switch keeps it

        status.Begin();
        Assert.Equal(WorkspaceStatus.RunningText, status.Outcome);
        status.Complete(new ExecuteQueryResult { Status = ExecuteStatus.Error, ErrorMessage = "x" });
        Assert.Equal(WorkspaceStatus.FailedText, status.Outcome);                   // a run keeps its own words
    }
}
