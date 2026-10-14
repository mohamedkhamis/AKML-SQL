using System.Net;
using System.Reflection;
using AkmlSql.Core;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Control;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.PostgreSql;
using AkmlSql.Engine.Transports;
using MessagePack;
using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T071): a PostgreSQL <c>ConnectionChanged</c> through the composed engine - the session takes the kind, its
/// persistent connection serves the next <c>ExecuteQuery</c>, a remembered password is saved after the server accepts it
/// and used afterwards, and the T-SQL parser's process-wide server version is left alone. Live: needs
/// <c>AKML_PG_TEST_CONNECTION</c>.
/// </summary>
[Collection("PostgreSqlCredentialStore")]
[Trait("Category", "PostgreSql")]
public class ConnectionChangedPgTests
{
    private static (InProcessTransport Transport, EngineComposition Engine) BuildEngine()
    {
        var engine = EngineComposition.Build();
        var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => engine.Router.RouteAsync(msg, engine.Context, ct);
        transport.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (transport, engine);
    }

    private static (string Host, int Port, string User, string Password) Admin()
    {
        var b = new NpgsqlConnectionStringBuilder(PgTestServer.AdminConnectionString);
        return (b.Host!, b.Port, b.Username!, b.Password!);
    }

    private static string Connection(string? password, string sslMode = "prefer")
    {
        var a = Admin();
        return PgConnectionString.Build(a.Host, a.Port, "postgres", a.User, password, sslMode, null);
    }

    private static Task ConnectAsync(InProcessTransport transport, string sessionId, string connectionString, string database = "postgres", bool remember = false)
    {
        var info = new ConnectionInfo
        {
            SessionId = sessionId,
            ConnectionString = connectionString,
            DatabaseName = database,
            Kind = (int)DatabaseKind.PostgreSql,
            RememberPassword = remember,
        };
        return transport.SendAsync(new RpcMessage
        {
            MessageType = MessageTypes.ConnectionChanged,
            RequestId = 0,
            Payload = MessagePackSerializer.Serialize(info),
        }, CancellationToken.None);
    }

    private static async Task<ExecuteQueryResult> ExecuteAsync(InProcessTransport transport, string sessionId, string sql, int requestId)
    {
        var request = new ExecuteQueryRequest
        {
            SessionId = sessionId,
            Sql = sql,
            MaxRows = 1000,
            CommandTimeoutSeconds = 30,
            QueryId = Guid.NewGuid().ToString("N"),
        };
        var response = await transport.SendAsync(new RpcMessage
        {
            MessageType = MessageTypes.ExecuteQuery,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(request),
        }, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.ExecuteQueryResult, response!.MessageType);
        return MessagePackSerializer.Deserialize<ExecuteQueryResult>(response.Payload!);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int seconds = 10)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            await Task.Delay(100);
        }
        return condition();
    }

    [SkippableFact]
    public async Task The_session_takes_the_kind_and_the_next_execute_runs_on_the_persistent_connection()
    {
        PgTestServer.SkipUnlessAvailable();
        var (transport, engine) = BuildEngine();
        var session = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, session, Connection(Admin().Password));

        Assert.Equal(DatabaseKind.PostgreSql, engine.Context.Sessions.KindOf(session));

        var first = await ExecuteAsync(transport, session, "SELECT 1 AS n, current_database() AS d", 1);
        Assert.Equal(ExecuteStatus.Ok, first.Status);
        Assert.Equal(new string?[] { "1", "postgres" }, first.ResultSets.Single().Rows.Single());
        Assert.Equal("postgres", first.CurrentDatabase);

        // The connection persists: a session setting and a transaction survive between executes.
        await ExecuteAsync(transport, session, "SET application_name = 'persists'", 2);
        var begun = await ExecuteAsync(transport, session, "BEGIN", 3);
        Assert.Equal((int)TransactionState.Open, begun.TransactionState);
        var again = await ExecuteAsync(transport, session, "SHOW application_name", 4);
        Assert.Equal("persists", again.ResultSets.Single().Rows.Single()[0]);
        var rolledBack = await ExecuteAsync(transport, session, "ROLLBACK", 5);
        Assert.Equal((int)TransactionState.None, rolledBack.TransactionState);
    }

    [SkippableFact]
    public async Task The_servers_facts_are_read_in_the_background()
    {
        PgTestServer.SkipUnlessAvailable();
        var (transport, engine) = BuildEngine();
        var session = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, session, Connection(Admin().Password));

        var known = await WaitForAsync(() => !string.IsNullOrEmpty(engine.Context.Sessions.GetSession(session)?.ServerVersionText));

        Assert.True(known, "The server version was never read.");
        var state = engine.Context.Sessions.GetSession(session)!;
        Assert.Matches(@"^\d+\.\d+", state.ServerVersionText);
        Assert.Contains("public", state.SearchPath);
    }

    [SkippableFact]
    public async Task The_T_SQL_parsers_server_version_is_left_alone()
    {
        PgTestServer.SkipUnlessAvailable();
        var (transport, engine) = BuildEngine();
        engine.Context.ParserService!.SetServerVersion(15);
        var field = typeof(TsqlParserService).GetField("_serverVersion", BindingFlags.NonPublic | BindingFlags.Instance)!;

        await ConnectAsync(transport, Guid.NewGuid().ToString("N"), Connection(Admin().Password));

        Assert.Equal(15, (int)field.GetValue(engine.Context.ParserService)!);
    }

    [SkippableFact]
    public async Task A_password_is_remembered_only_after_the_server_accepts_it_and_is_used_afterwards()
    {
        PgTestServer.SkipUnlessAvailable();
        var a = Admin();
        var resolver = new PgCredentialResolver();
        resolver.Forget(a.Host, a.Port, a.User);
        try
        {
            var (transport, _) = BuildEngine();

            // Asked to remember a wrong password: the server refuses it and nothing is stored.
            await ConnectAsync(transport, Guid.NewGuid().ToString("N"), Connection("wrong-password"), remember: true);
            await Task.Delay(1500);
            Assert.False(resolver.Has(a.Host, a.Port, a.User));

            // Not asked: nothing is stored even though the server accepts it.
            await ConnectAsync(transport, Guid.NewGuid().ToString("N"), Connection(a.Password), remember: false);
            await Task.Delay(1500);
            Assert.False(resolver.Has(a.Host, a.Port, a.User));

            // Asked and accepted: stored.
            await ConnectAsync(transport, Guid.NewGuid().ToString("N"), Connection(a.Password), remember: true);
            Assert.True(await WaitForAsync(() => resolver.Has(a.Host, a.Port, a.User)));

            // A later connection without a password uses it.
            var session = Guid.NewGuid().ToString("N");
            await ConnectAsync(transport, session, Connection(null));
            var result = await ExecuteAsync(transport, session, "SELECT 'remembered'", 1);
            Assert.Equal(ExecuteStatus.Ok, result.Status);
            Assert.Equal("remembered", result.ResultSets.Single().Rows.Single()[0]);
        }
        finally
        {
            resolver.Forget(a.Host, a.Port, a.User);
        }
    }

    [SkippableFact]
    public async Task A_bridge_request_the_engine_would_not_open_is_ignored_and_creates_no_session()
    {
        PgTestServer.SkipUnlessAvailable();
        var engine = EngineComposition.Build();
        var session = Guid.NewGuid().ToString("N");
        var info = new ConnectionInfo
        {
            SessionId = session,
            ConnectionString = "Host=db1;Username=u;Integrated Security=true",
            DatabaseName = "postgres",
            Kind = (int)DatabaseKind.PostgreSql,
        };

        using (BridgeSourceIp.Set(IPAddress.Loopback))
        {
            await new ConnectionChangedHandler().HandleAsync(info, engine.Context, CancellationToken.None);
        }

        Assert.Null(engine.Context.Sessions.GetSession(session));
    }

    [SkippableFact]
    public async Task Parse_checks_the_script_without_a_connection_to_the_server()
    {
        PgTestServer.SkipUnlessAvailable();
        var (transport, _) = BuildEngine();
        var session = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, session, Connection(Admin().Password));

        var request = new ExecuteQueryRequest { SessionId = session, Sql = "SELECT 1;\nSELEC 2;", QueryId = "parse1" };
        var response = await transport.SendAsync(new RpcMessage
        {
            MessageType = MessageTypes.ExecuteParse,
            RequestId = 9,
            Payload = MessagePackSerializer.Serialize(request),
        }, CancellationToken.None);

        Assert.Equal(MessageTypes.ExecuteParseResult, response!.MessageType);
        var result = MessagePackSerializer.Deserialize<ExecuteQueryResult>(response.Payload!);
        var error = Assert.Single(result.MessageDetails, m => m.Kind == ExecuteMessageKind.Error);
        Assert.Equal("42601", error.SqlState);
        Assert.Equal(2, error.Line);
    }

    [SkippableFact]
    public async Task Reconnecting_as_another_role_reads_the_catalog_again_as_that_role()
    {
        PgTestServer.SkipUnlessAvailable();
        await using var database = await PgTestServer.CreateDatabaseAsync(
            "CREATE SCHEMA secret; CREATE TABLE secret.vault (k int); CREATE SCHEMA shown; CREATE TABLE shown.t (k int);" +
            "GRANT USAGE ON SCHEMA shown TO PUBLIC; GRANT SELECT ON shown.t TO PUBLIC;");
        var role = "akml_reconnect_" + database.Name.Substring(7);
        await using (var admin = new NpgsqlConnection(PgTestServer.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE ROLE " + role + " LOGIN PASSWORD 'pw'", admin);
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            var (transport, engine) = BuildEngine();
            var session = Guid.NewGuid().ToString("N");
            var asAdmin = database.ConnectionString;
            var asRole = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Username = role, Password = "pw" }.ConnectionString;

            await ConnectAsync(transport, session, asAdmin, database.Name);
            Assert.True(await WaitForAsync(() => engine.Context.SchemaCache.GetCache(session, database.Name)?.Phase == AkmlSql.Engine.Schema.PopulationPhase.PhaseB));
            Assert.Empty(engine.Context.SchemaCache.GetCache(session, database.Name)!.InaccessibleSchemas);

            await ConnectAsync(transport, session, asRole, database.Name);

            Assert.True(await WaitForAsync(() => engine.Context.SchemaCache.GetCache(session, database.Name) is { Phase: AkmlSql.Engine.Schema.PopulationPhase.PhaseB } c
                                                 && c.InaccessibleSchemas.Contains("secret")));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanup = new NpgsqlConnection(PgTestServer.AdminConnectionString);
            await cleanup.OpenAsync();
            await using var drop = new NpgsqlCommand("DROP ROLE IF EXISTS " + role, cleanup);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
