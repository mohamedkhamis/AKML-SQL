using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T126, FR-090, FR-092, SC-010). A PostgreSQL session that asks the engine for completion, hover, signature
/// help, wildcard expansion, statement boundaries, formatting, analysis, the safety check and execution leaves the T-SQL parser
/// untouched: the process-wide count of its tokenizing and parsing calls does not move. The same requests on a SQL Server session
/// do move it, so the count is a real instrument and not a constant. Every request goes through the real router, so the guard,
/// the typed adapters and the handlers are all in the path.
/// </summary>
[Collection(TsqlParserCounterCollection.Name)]
public sealed class NoTsqlOnPostgresSessionTests
{
    private const string PgSql = "SELECT c.customer_id, count(*) FROM customers c JOIN orders o ON o.customer_id = c.customer_id WHERE c.country = 'UK' GROUP BY 1 LIMIT 5;";

    private readonly EngineComposition _engine = EngineComposition.Build();
    private readonly string _pg = "pg-" + Guid.NewGuid().ToString("N");
    private readonly string _sql = "sql-" + Guid.NewGuid().ToString("N");
    private int _requestId;

    public NoTsqlOnPostgresSessionTests()
    {
        // A host that refuses at once: execution is attempted, and fails fast, without a server.
        _engine.Context.Sessions.UpdateSession(new ConnectionInfo
        {
            SessionId = _pg, ConnectionString = "Host=127.0.0.1;Port=1;Database=northwind;Username=u;Password=p;Timeout=2;Pooling=false",
            DatabaseName = "northwind", Kind = (int)DatabaseKind.PostgreSql,
        });
        _engine.Context.Sessions.UpdateSession(new ConnectionInfo
        {
            SessionId = _sql, ConnectionString = "Server=127.0.0.1,1;Database=northwind;User Id=u;Password=p;Connect Timeout=2;Encrypt=false",
            DatabaseName = "northwind", Kind = (int)DatabaseKind.SqlServer,
        });
    }

    /// <summary>Requests that moved the count, with how far: which one reached the T-SQL parser is the whole point.</summary>
    private readonly List<string> _moved = new();

    private async Task Send<T>(int type, T request) where T : class
    {
        var before = TsqlParserService.TotalInvocations;
        var message = new RpcMessage { MessageType = type, RequestId = ++_requestId, Payload = MessagePackSerializer.Serialize(request) };
        var response = await _engine.Router.RouteAsync(message, _engine.Context, CancellationToken.None);
        Assert.NotNull(response);   // every request is answered, with a result or with the error envelope; none is lost
        var moved = TsqlParserService.TotalInvocations - before;
        if (moved > 0) _moved.Add($"{typeof(T).Name} (+{moved})");
    }

    private async Task AskEverything(string session, string sql)
    {
        _engine.Context.Sessions.UpdateDocument(new DocumentChange { SessionId = session, ChangeType = 0, FullText = sql });
        var inCount = sql.IndexOf("count(", StringComparison.Ordinal) + "count(".Length;
        var onTable = sql.IndexOf("customers", StringComparison.Ordinal) + 3;

        await Send(MessageTypes.RequestCompletion, new CompletionRequest { SessionId = session, CursorOffset = sql.IndexOf("WHERE", StringComparison.Ordinal) + 6 });
        await Send(MessageTypes.RequestQuickInfo, new QuickInfoRequest { SessionId = session, CursorOffset = onTable });
        await Send(MessageTypes.RequestSignatureHelp, new SignatureRequest { SessionId = session, CursorOffset = inCount });
        await Send(MessageTypes.WildcardExpansion, new WildcardExpansionRequest { SessionId = session, CursorOffset = 8, DocumentText = sql });
        await Send(MessageTypes.StatementBoundary, new StatementBoundaryRequest { SessionId = session, SqlText = sql, CursorOffset = 10 });
        await Send(MessageTypes.FormatDocument, new FormatRequest { SessionId = session, Text = sql });
        await Send(MessageTypes.RequestAnalyze, new CodeAnalysisRequest { SessionId = session, RequestId = "r", DocumentText = sql, DocumentVersion = 1 });
        await Send(MessageTypes.ExecuteQuery, new ExecuteQueryRequest { SessionId = session, Sql = sql, QueryId = "q" + _requestId, CommandTimeoutSeconds = 3 });
    }

    private async Task<TResponse> Ask<TRequest, TResponse>(int type, TRequest request) where TRequest : class
    {
        var message = new RpcMessage { MessageType = type, RequestId = ++_requestId, Payload = MessagePackSerializer.Serialize(request) };
        var response = await _engine.Router.RouteAsync(message, _engine.Context, CancellationToken.None);
        Assert.NotNull(response);
        return MessagePackSerializer.Deserialize<TResponse>(response!.Payload!);
    }

    // Spec 042, user story 7: a PostgreSQL session is formatted by the PostgreSQL formatter. The T-SQL parser count in the test below still
    // stays where it was, which is what proves the T-SQL formatter never saw the text.
    [Fact]
    public async Task A_PostgreSQL_session_is_formatted_by_the_PostgreSQL_formatter()
    {
        const string sql = "select  *   from customers   where country='UK'";
        var before = TsqlParserService.TotalInvocations;

        var document = await Ask<FormatRequest, FormatResponse>(MessageTypes.FormatDocument, new FormatRequest { SessionId = _pg, Text = sql });
        var selection = await Ask<FormatSelectionRequest, FormatSelectionResponse>(
            MessageTypes.FormatSelection, new FormatSelectionRequest { SessionId = _pg, Text = sql, SelectionStart = 0, SelectionEnd = sql.Length });
        var casing = await Ask<FormatActionRequest, FormatActionResponse>(
            MessageTypes.FormatAction, new FormatActionRequest { SessionId = _pg, Text = sql, ActionType = (int)FormatActionType.CasingOnly });
        var refused = await Ask<FormatActionRequest, FormatActionResponse>(
            MessageTypes.FormatAction, new FormatActionRequest { SessionId = _pg, Text = sql, ActionType = (int)FormatActionType.ConvertOldStyleJoins });

        Assert.True(document.Success, string.Join("; ", document.Diagnostics?.Select(d => d.Message) ?? Array.Empty<string>()));
        Assert.StartsWith("SELECT", document.FormattedText);
        Assert.Contains("'UK'", document.FormattedText);
        Assert.NotEqual(sql, document.FormattedText);
        Assert.True(selection.Success);
        Assert.Equal(0, selection.OriginalStart);
        Assert.Equal(sql.Length, selection.OriginalEnd);
        Assert.True(casing.Success);
        Assert.Equal("SELECT  *   FROM customers   WHERE country='UK'", casing.FormattedText);   // case only: the white space is untouched
        Assert.False(refused.Success);
        Assert.Equal("Not available for PostgreSQL", refused.ErrorMessage);
        Assert.Equal(sql, refused.FormattedText);
        Assert.Equal(before, TsqlParserService.TotalInvocations);
    }

    [Fact]
    public async Task A_SQL_Server_session_is_still_formatted_as_before()
    {
        var document = await Ask<FormatRequest, FormatResponse>(MessageTypes.FormatDocument, new FormatRequest { SessionId = _sql, Text = "select  *   from customers" });

        Assert.True(document.Success);
        Assert.NotEqual("select  *   from customers", document.FormattedText);
    }

    [Fact]
    public async Task Analysis_of_a_PostgreSQL_session_reports_no_findings_instead_of_T_SQL_syntax_errors()
    {
        var response = await Ask<CodeAnalysisRequest, CodeAnalysisResponse>(
            MessageTypes.RequestAnalyze, new CodeAnalysisRequest { SessionId = _pg, RequestId = "r1", DocumentText = "SELECT 1 FROM t LIMIT 5;", DocumentVersion = 9 });

        Assert.Equal("r1", response.RequestId);
        Assert.Empty(response.Issues);
        Assert.Equal(9, response.AnalyzedVersion);
    }

    [Fact]
    public async Task A_PostgreSQL_session_asking_for_everything_never_reaches_the_T_SQL_parser()
    {
        var before = TsqlParserService.TotalInvocations;

        await AskEverything(_pg, PgSql);
        await Send(MessageTypes.SafetyCheck, new SafetyCheckRequest { SqlText = "DELETE FROM customers;", Kind = (int)DatabaseKind.PostgreSql });

        Assert.True(_moved.Count == 0, "These PostgreSQL requests reached the T-SQL parser: " + string.Join(", ", _moved));
        Assert.Equal(before, TsqlParserService.TotalInvocations);
    }

    [Fact]
    public async Task The_same_requests_on_a_SQL_Server_session_do_reach_it_so_the_count_is_a_real_instrument()
    {
        var before = TsqlParserService.TotalInvocations;

        await AskEverything(_sql, "SELECT c.CustomerID, COUNT(*) FROM Customers c JOIN Orders o ON o.CustomerID = c.CustomerID WHERE c.Country = 'UK' GROUP BY c.CustomerID;");

        Assert.True(TsqlParserService.TotalInvocations > before, "a SQL Server session should have used the T-SQL parser");
    }

    [Fact]
    public async Task Surfaces_without_a_PostgreSQL_form_are_refused_without_reaching_the_parser()
    {
        var before = TsqlParserService.TotalInvocations;
        _engine.Context.Sessions.UpdateDocument(new DocumentChange { SessionId = _pg, ChangeType = 0, FullText = PgSql });

        await Send(MessageTypes.RequestRefactorPreview, new RefactorPreviewRequest { SessionId = _pg, DocumentText = PgSql });
        await Send(MessageTypes.FindInvalidObjects, new FindInvalidObjectsRequest { SessionId = _pg });
        await Send(MessageTypes.DocumentOutline, new DocumentOutlineRequest { SessionId = _pg });
        await Send(MessageTypes.ScriptAs, new ScriptAsRequest { SessionId = _pg });

        Assert.True(_moved.Count == 0, "These PostgreSQL requests reached the T-SQL parser: " + string.Join(", ", _moved));
        Assert.Equal(before, TsqlParserService.TotalInvocations);
    }
}
