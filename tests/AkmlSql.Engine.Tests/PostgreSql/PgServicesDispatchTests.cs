using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Completion;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Snippets.Models;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T106, FR-061, FR-062, FR-066, FR-067, FR-069) - a PostgreSQL session's completion, hover, signature help,
/// wildcard expansion, snippets and safety check go to the PostgreSQL services, and a SQL Server session's still go to the T-SQL
/// ones. No server is needed: the session and the schema cache are filled in by hand.
/// </summary>
public sealed class PgServicesDispatchTests
{
    private readonly EngineComposition _engine = EngineComposition.Build();
    private readonly string _pgSession = "pg-" + Guid.NewGuid().ToString("N");
    private readonly string _sqlSession = "sql-" + Guid.NewGuid().ToString("N");

    public PgServicesDispatchTests()
    {
        _engine.Context.Sessions.UpdateSession(new ConnectionInfo { SessionId = _pgSession, ConnectionString = "Host=h;Database=northwind", DatabaseName = "northwind", Kind = (int)DatabaseKind.PostgreSql });
        _engine.Context.Sessions.GetSession(_pgSession)!.ServerVersionText = "18.1";
        _engine.Context.Sessions.UpdateSession(new ConnectionInfo { SessionId = _sqlSession, ConnectionString = "Server=s;Database=northwind", DatabaseName = "northwind", Kind = (int)DatabaseKind.SqlServer });

        Seed(_engine.Context.SchemaCache.GetOrCreateCache(_pgSession, "northwind"), "public", "customers", "customer_id", "company_name");
        Seed(_engine.Context.SchemaCache.GetOrCreateCache(_sqlSession, "northwind"), "dbo", "Customers", "CustomerID", "CompanyName");
    }

    private static void Seed(DatabaseCache cache, string schema, string table, params string[] columns)
    {
        cache.Phase = PopulationPhase.Complete;
        var entry = cache.Schemas.GetOrAdd(schema, s => new SchemaEntry { SchemaName = s });
        var o = new DatabaseObject { ObjectId = 1, SchemaName = schema, ObjectName = table, ObjectType = DbObjectType.Table, ColumnsLoaded = true };
        for (var i = 0; i < columns.Length; i++)
            o.Columns.Add(new Column { ColumnId = i + 1, ColumnName = columns[i], TypeName = "text", IsPrimaryKey = i == 0 });
        entry.Objects.Add(o);
    }

    private void Document(string session, string text) =>
        _engine.Context.Sessions.UpdateDocument(new DocumentChange { SessionId = session, ChangeType = 0, FullText = text });

    private static string Names(CompletionResponse r) => string.Join("|", r.Items.Select(i => i.DisplayText));

    // ── Completion ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_PostgreSQL_session_is_completed_by_the_PostgreSQL_engine()
    {
        Document(_pgSession, "SELECT * FROM ");
        var handler = new CompletionHandler(new Engine.Completion.CompletionEngine(_engine.Context.ParserService!), () => _engine.Context.EnsureSettings(), new Engine.PostgreSql.PgServices());

        var r = await handler.HandleAsync(new CompletionRequest { SessionId = _pgSession, CursorOffset = 14 }, _engine.Context, default);

        Assert.Contains(r.Items, i => i.DisplayText == "customers" && i.InsertText == "customers");
        Assert.DoesNotContain(r.Items, i => i.DisplayText == "dbo");
        Assert.Contains(r.Items, i => i.DisplayText == "LATERAL");     // a PostgreSQL FROM word
        Assert.DoesNotContain(r.Items, i => i.DisplayText is "GO" or "NOCOUNT");
    }

    [Fact]
    public async Task A_SQL_Server_session_still_gets_the_t_sql_engine()
    {
        Document(_sqlSession, "SELECT * FROM ");
        var handler = new CompletionHandler(new Engine.Completion.CompletionEngine(_engine.Context.ParserService!), () => _engine.Context.EnsureSettings(), new Engine.PostgreSql.PgServices());

        var r = await handler.HandleAsync(new CompletionRequest { SessionId = _sqlSession, CursorOffset = 14 }, _engine.Context, default);

        Assert.Contains(r.Items, i => i.DisplayText.Contains("Customers", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Items, i => i.DisplayText == "LATERAL");
    }

    [Fact]
    public async Task Without_the_PostgreSQL_services_a_PostgreSQL_session_falls_back_as_before()
    {
        Document(_pgSession, "SELECT * FROM ");
        var handler = new CompletionHandler(new Engine.Completion.CompletionEngine(_engine.Context.ParserService!), () => _engine.Context.EnsureSettings());

        var r = await handler.HandleAsync(new CompletionRequest { SessionId = _pgSession, CursorOffset = 14 }, _engine.Context, default);

        Assert.NotNull(r);   // the dialect is off: the router's guard keeps these requests away, and this handler does not crash
    }

    [Fact]
    public async Task The_servers_release_gates_what_is_offered()
    {
        _engine.Context.Sessions.GetSession(_pgSession)!.ServerVersionText = "14.13";
        Document(_pgSession, "");
        var handler = new CompletionHandler(new Engine.Completion.CompletionEngine(_engine.Context.ParserService!), () => _engine.Context.EnsureSettings(), new Engine.PostgreSql.PgServices());

        var r14 = await handler.HandleAsync(new CompletionRequest { SessionId = _pgSession, CursorOffset = 0 }, _engine.Context, default);
        _engine.Context.Sessions.GetSession(_pgSession)!.ServerVersionText = "18.1";
        var r18 = await handler.HandleAsync(new CompletionRequest { SessionId = _pgSession, CursorOffset = 0 }, _engine.Context, default);

        Assert.DoesNotContain(r14.Items, i => i.DisplayText == "MERGE");
        Assert.Contains(r18.Items, i => i.DisplayText == "MERGE");
    }

    [Theory]
    [InlineData("18.1", 18)]
    [InlineData("14.13", 14)]
    [InlineData("17", 17)]
    [InlineData("", 18)]
    [InlineData("garbage", 18)]
    [InlineData(null, 18)]
    public void The_servers_major_release_is_read_from_its_version_text(string? text, int expected)
    {
        var session = new SessionState { ServerVersionText = text ?? string.Empty };

        Assert.Equal(expected, Engine.PostgreSql.PgServices.ServerMajor(session));
        Assert.Equal(18, Engine.PostgreSql.PgServices.ServerMajor(null));
    }

    // ── Hover, signature help, wildcard ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Hover_on_a_PostgreSQL_session_describes_with_PostgreSQL_words()
    {
        const string sql = "SELECT * FROM customers";
        Document(_pgSession, sql);
        var handler = new QuickInfoHandler(_engine.Context.ParserService!, new Engine.Completion.Providers.QuickInfoProvider(), new Engine.PostgreSql.PgServices());

        var r = await handler.HandleAsync(new QuickInfoRequest { SessionId = _pgSession, CursorOffset = sql.IndexOf("customers", StringComparison.Ordinal) }, _engine.Context, default);

        Assert.Equal("Table", r.ObjectType);
        Assert.Equal("public.customers", r.Header);
    }

    [Fact]
    public async Task Signature_help_on_a_PostgreSQL_session_comes_from_the_reference()
    {
        const string sql = "SELECT date_trunc('day', ";
        Document(_pgSession, sql);
        var handler = new SignatureHelpHandler(_engine.Context.ParserService!, new Engine.Completion.Providers.SignatureProvider(), new Engine.PostgreSql.PgServices());

        var r = await handler.HandleAsync(new SignatureRequest { SessionId = _pgSession, CursorOffset = sql.Length }, _engine.Context, default);

        Assert.Equal("date_trunc", r.FunctionName);
        Assert.Equal(1, r.ActiveParameter);
        Assert.NotEmpty(r.Overloads!);
    }

    [Fact]
    public async Task Wildcard_expansion_on_a_PostgreSQL_session_uses_the_postgresql_names()
    {
        const string sql = "SELECT * FROM customers";
        Document(_pgSession, sql);
        var handler = new AkmlSql.Engine.Handlers.Completion.WildcardExpansionHandler(new AkmlSql.Engine.Completion.WildcardExpansionHandler(_engine.Context.ParserService!), new AkmlSql.Engine.PostgreSql.PgServices());

        var r = await handler.HandleAsync(new WildcardExpansionRequest { SessionId = _pgSession, CursorOffset = 8, DocumentText = sql }, _engine.Context, default);

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "customer_id", "company_name" }, r.Tables.Single().Columns.Select(c => c.ColumnName).ToArray());
    }

    // ── Snippets ────────────────────────────────────────────────────────────────────────────────

    private static SnippetRequestHandler SnippetsWithBothPacks(string root)
    {
        var builtIn = Path.Combine(root, "builtin");
        Directory.CreateDirectory(builtIn);
        File.WriteAllText(Path.Combine(builtIn, "sel.akmlsnippet"),
            "{\"metadata\":{\"id\":\"b-sel\",\"shortcode\":\"sel\",\"name\":\"SELECT\",\"description\":\"d\",\"category\":\"Query\",\"context\":[\"global\"]},\"variables\":[],\"body\":[\"SELECT $CURSOR$ FROM dbo.t\"]}");
        File.WriteAllText(Path.Combine(builtIn, "pg-sel.akmlsnippet"),
            "{\"metadata\":{\"id\":\"b-pg-sel\",\"shortcode\":\"sel\",\"name\":\"SELECT\",\"description\":\"d\",\"category\":\"Query\",\"context\":[\"global\"],\"dialect\":\"postgresql\"},\"variables\":[],\"body\":[\"SELECT $CURSOR$ FROM t LIMIT 100\"]}");
        return new SnippetRequestHandler(Path.Combine(root, "personal"), builtIn);
    }

    [Fact]
    public void A_shortcode_expands_to_the_snippet_of_the_sessions_dialect()
    {
        var root = Path.Combine(Path.GetTempPath(), "akml-snip-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = SnippetsWithBothPacks(root);
            var request = new SnippetExpandRequest { Shortcode = "sel" };

            var sqlServer = handler.HandleExpand(request);
            var postgres = handler.HandleExpand(request, dialect: SnippetDialects.PostgreSql);

            Assert.Contains("dbo.t", sqlServer.ExpandedText);
            Assert.Contains("LIMIT 100", postgres.ExpandedText);
            Assert.DoesNotContain("dbo", postgres.ExpandedText);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_list_is_cut_to_the_dialect_asked_for()
    {
        var root = Path.Combine(Path.GetTempPath(), "akml-snip-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = SnippetsWithBothPacks(root);

            var sqlServer = handler.HandleList(new SnippetListRequest());
            var postgres = handler.HandleList(new SnippetListRequest { Kind = (int)DatabaseKind.PostgreSql });

            Assert.Equal(new[] { "b-sel" }, sqlServer.Snippets.Select(s => s.Id).ToArray());
            Assert.Equal(new[] { "b-pg-sel" }, postgres.Snippets.Select(s => s.Id).ToArray());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_snippet_file_without_a_dialect_is_a_sql_server_snippet()
    {
        Assert.Equal(SnippetDialects.SqlServer, new SnippetMetadata().Dialect);
        Assert.True(SnippetDialects.Is(new Snippet(), SnippetDialects.SqlServer));
        Assert.False(SnippetDialects.Is(new Snippet(), SnippetDialects.PostgreSql));
    }

    [Fact]
    public void The_engine_ships_the_postgresql_pack_beside_the_sql_server_one()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "snippets");

        Assert.True(Directory.Exists(folder));
        Assert.Contains(Directory.GetFiles(folder, "pg-*.akmlsnippet"), f => Path.GetFileName(f) == "pg-sel.akmlsnippet");
        Assert.Contains(Directory.GetFiles(folder, "*.akmlsnippet"), f => Path.GetFileName(f) == "sel.akmlsnippet");
    }

    // ── The safety check ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_safety_check_reads_a_PostgreSQL_script_with_the_PostgreSQL_check()
    {
        var handler = new Engine.Safety.SafetyCheckHandler(_engine.Context.ParserService!);
        var request = new SafetyCheckRequest { SqlText = "DELETE FROM orders; TRUNCATE order_details; DROP TABLE x;", Kind = (int)DatabaseKind.PostgreSql };

        var reply = await handler.HandleAsync(new RpcMessage { MessageType = MessageTypes.SafetyCheck, RequestId = 1, Payload = MessagePack.MessagePackSerializer.Serialize(request) });
        var response = MessagePack.MessagePackSerializer.Deserialize<SafetyCheckResponse>(reply!.Payload!);

        Assert.True(response.RequiresConfirmation);
        Assert.Equal(3, response.Warnings.Length);
    }

    [Fact]
    public async Task A_SQL_Server_script_is_still_read_by_the_t_sql_check()
    {
        var handler = new Engine.Safety.SafetyCheckHandler(_engine.Context.ParserService!);
        var request = new SafetyCheckRequest { SqlText = "DELETE FROM orders", Kind = (int)DatabaseKind.SqlServer };

        var reply = await handler.HandleAsync(new RpcMessage { MessageType = MessageTypes.SafetyCheck, RequestId = 1, Payload = MessagePack.MessagePackSerializer.Serialize(request) });
        var response = MessagePack.MessagePackSerializer.Deserialize<SafetyCheckResponse>(reply!.Payload!);

        Assert.True(response.RequiresConfirmation);
        Assert.Single(response.Warnings);
    }
}
