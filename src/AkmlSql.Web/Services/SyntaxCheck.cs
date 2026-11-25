using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 047 (WEB-005, contracts/analysis-display.md §4) — Parse (Ctrl+F5) for a T-SQL tab works without a connection: the
/// server checks the script when the tab is connected to an engine that understands Parse (<c>execute.v2</c>), else the
/// browser's own T-SQL parser does. Either way the result reads like the server's (Messages, the red mark on the first
/// error, a status that says "Parse failed: …"). Pure, so the decisions are tested without a page.
/// </summary>
public static class SyntaxCheck
{
    /// <summary>The Messages line that says where the check ran.</summary>
    public const string CheckedInBrowser = "Checked in the browser; the server was not asked.";

    // The server's number and level for "Incorrect syntax near …", which is what a browser-found parse error stands for.
    private const int SyntaxErrorNumber = 102;
    private const int SyntaxErrorSeverity = 15;

    private static readonly TsqlParserService Parser = new();

    /// <summary>
    /// Whether Parse can run: a T-SQL tab always can (in the browser when not on the server); a PostgreSQL tab needs its
    /// connection to an engine that understands Parse, as before.
    /// </summary>
    public static bool CanParse(bool running, bool postgres, bool connected, bool engineParses) =>
        !running && (!postgres || (connected && engineParses));

    /// <summary>Whether the server checks it: a connected tab, an engine that understands Parse, and the link open.</summary>
    public static bool OnServer(bool connected, bool engineParses, bool linkOpen) => connected && engineParses && linkOpen;

    /// <summary>
    /// The browser's check of <paramref name="sql"/> as a Parse reply: an info line saying where it ran, then one error per
    /// parse error in the server's form (Msg 102, Level 15, the line within the text). <c>GO n</c> repeat counts, which
    /// SSMS accepts and ScriptDom alone rejects, are not errors.
    /// </summary>
    public static ExecuteQueryResult InBrowser(string sql, string queryId)
    {
        sql ??= string.Empty;
        Parser.Parse(sql, out var errors);
        var tokens = errors.Count > 0 ? Parser.GetTokenStream(sql) : null;

        var messages = new List<ExecuteMessageDto> { new() { Kind = ExecuteMessageKind.Info, Text = CheckedInBrowser } };
        foreach (var e in errors)
        {
            if (tokens != null && TsqlParserService.IsGoRepeatCount(e, tokens)) continue;
            messages.Add(new ExecuteMessageDto
            {
                Kind = ExecuteMessageKind.Error,
                Number = SyntaxErrorNumber,
                Severity = SyntaxErrorSeverity,
                State = 1,
                Line = e.Line,
                Text = e.Message,
            });
        }

        var failed = messages.Count > 1;
        return new ExecuteQueryResult
        {
            QueryId = queryId ?? string.Empty,
            Status = failed ? ExecuteStatus.Error : ExecuteStatus.Ok,
            ErrorMessage = failed ? messages[1].Text : null,
            MessageDetails = messages.ToArray(),
            Messages = messages.Select(m => m.Text).ToArray(),
            CompletedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    /// <summary>The status words for a Parse reply, from the server or the browser.</summary>
    public static string Outcome(ExecuteQueryResult result)
    {
        var errors = result?.MessageDetails?.Count(m => m.Kind == ExecuteMessageKind.Error) ?? 0;
        if (errors == 0 && result?.Status is not (null or ExecuteStatus.Ok)) errors = 1;   // an error with no message of its own
        return errors switch
        {
            0 => WorkspaceStatus.ParseSucceeded,
            1 => WorkspaceStatus.ParseFailedOne,
            _ => string.Format(System.Globalization.CultureInfo.InvariantCulture, WorkspaceStatus.ParseFailedMany, errors),
        };
    }

    /// <summary>
    /// The first error that has a document line (the same mapping as a click in Messages), with the text the red mark shows,
    /// or null when no error has one.
    /// </summary>
    public static (int Line, ExecuteMessageDto Message)? FirstError(ExecuteQueryResult result, int selectionStartLine)
    {
        foreach (var message in result?.MessageDetails ?? Array.Empty<ExecuteMessageDto>())
        {
            if (ExecuteLineMapper.DocumentLine(selectionStartLine, result!, message) is int line)
                return (line, message);
        }
        return null;
    }
}
