using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Handlers.Control;
using AkmlSql.Engine.Pairing;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 041 (T177, T178, research R25, R26, N27, contracts/ipc.md) — the toolbar's database selector, without a SQL
/// Server: the database list for a session uses the connection the engine already holds (the browser never resends
/// a password) and re-runs the bridge guard; a database switch refuses an unknown session, a name that could
/// inject connection-string keywords, and a target the guard refuses. The switch itself is covered against SQL in
/// <c>ExecuteQueryIntegrationTests</c>.
/// </summary>
public sealed class SessionDatabaseHandlersTests
{
    // A remote server with Windows authentication: the guard refuses it for a bridge request.
    private const string RemoteIntegrated = "Server=tcp:db.example.com,1433;Database=Sales;Integrated Security=true;Encrypt=false;Connect Timeout=1";

    private static RpcContext CreateContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        SettingsLoader = () => new AppSettings(),
        SessionConnections = new SessionConnectionRegistry(),
    };

    private static void Connect(RpcContext ctx, string sessionId, string connectionString, string database = "Sales") =>
        ctx.Sessions.UpdateSession(new ConnectionInfo { SessionId = sessionId, ConnectionString = connectionString, DatabaseName = database, ServerVersion = 160 });

    // ── ListDatabases (94) with a SessionId ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_session_without_a_connection_string_lists_with_the_sessions_connection_and_the_guard_judges_it()
    {
        var ctx = CreateContext();
        Connect(ctx, "web-1", RemoteIntegrated);

        ListDatabasesResponse response;
        using (BridgeSourceIp.Set(IPAddress.Loopback))
            response = await new ListDatabasesHandler().HandleAsync(new ListDatabasesRequest { SessionId = "web-1" }, ctx, CancellationToken.None);

        // The refusal proves the session's own connection string was used and judged (no SQL needed).
        Assert.False(response.Ok);
        Assert.Equal(BridgeSqlTargetGuard.AmbientIdentityRefused, response.ErrorMessage);
    }

    [Fact]
    public async Task An_unknown_session_gets_an_error()
    {
        var response = await new ListDatabasesHandler().HandleAsync(new ListDatabasesRequest { SessionId = "nobody" }, CreateContext(), CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Contains("not connected", response.ErrorMessage);
    }

    [Fact]
    public async Task A_disconnected_session_gets_an_error()
    {
        var ctx = CreateContext();
        Connect(ctx, "web-1", RemoteIntegrated);
        ctx.Sessions.GetSession("web-1")!.IsConnected = false;

        var response = await new ListDatabasesHandler().HandleAsync(new ListDatabasesRequest { SessionId = "web-1" }, ctx, CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Contains("not connected", response.ErrorMessage);
    }

    [Fact]
    public async Task Neither_a_connection_string_nor_a_session_is_still_refused()
    {
        var response = await new ListDatabasesHandler().HandleAsync(new ListDatabasesRequest(), CreateContext(), CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal("No connection string supplied.", response.ErrorMessage);
    }

    // ── ChangeDatabase (217) ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Switching_an_unknown_session_gets_an_error()
    {
        var response = await new ChangeDatabaseHandler().HandleAsync(
            new ChangeDatabaseRequest { SessionId = "nobody", DatabaseName = "master" }, CreateContext(), CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Contains("not connected", response.ErrorMessage);
    }

    [Theory]
    [InlineData("line\nbreak")]
    [InlineData("tab\tchar")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_name_SQL_Server_could_not_have_given_a_database_is_refused(string name)
    {
        var ctx = CreateContext();
        Connect(ctx, "web-1", "Server=localhost;Database=Sales;Integrated Security=true;Connect Timeout=1");

        var response = await new ChangeDatabaseHandler().HandleAsync(
            new ChangeDatabaseRequest { SessionId = "web-1", DatabaseName = name }, ctx, CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Contains("database name", response.ErrorMessage);
        Assert.Equal("Sales", ctx.Sessions.GetSession("web-1")!.DatabaseName);   // nothing changed
    }

    [Theory]
    [InlineData("O'Brien")]
    [InlineData("Sales;2025")]
    [InlineData("a=b")]
    [InlineData("say \"hi\"")]
    [InlineData(" Archive")]
    [InlineData("Archive ")]
    public void A_legal_database_name_with_quotes_semicolons_or_spaces_is_accepted(string name)
    {
        Assert.True(ChangeDatabaseHandler.IsSafeDatabaseName(name));
    }

    [Fact]
    public void A_name_over_128_characters_is_refused()
    {
        Assert.False(ChangeDatabaseHandler.IsSafeDatabaseName(new string('d', 129)));
        Assert.True(ChangeDatabaseHandler.IsSafeDatabaseName(new string('d', 128)));
    }

    [Theory]
    [InlineData("master;Integrated Security=false")]
    [InlineData("x';Password=p")]
    [InlineData("say \"hi\";User ID=sa")]
    public void A_name_holding_connection_string_syntax_stays_one_value(string name)
    {
        // Why the name needs no character filter: the builder quotes the value, so it never adds keywords.
        var built = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            "Server=localhost;Database=Sales;Integrated Security=true") { InitialCatalog = name }.ConnectionString;
        var back = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(built);

        Assert.Equal(name, back.InitialCatalog);
        Assert.True(back.IntegratedSecurity);
        Assert.Equal(string.Empty, back.Password);
        Assert.Equal(string.Empty, back.UserID);
    }

    [Fact]
    public async Task A_target_the_guard_refuses_is_not_switched_to()
    {
        var ctx = CreateContext();
        Connect(ctx, "web-1", RemoteIntegrated);

        ChangeDatabaseResponse response;
        using (BridgeSourceIp.Set(IPAddress.Loopback))
            response = await new ChangeDatabaseHandler().HandleAsync(
                new ChangeDatabaseRequest { SessionId = "web-1", DatabaseName = "master" }, ctx, CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal(BridgeSqlTargetGuard.AmbientIdentityRefused, response.ErrorMessage);
        Assert.Equal("Sales", ctx.Sessions.GetSession("web-1")!.DatabaseName);
    }

    [Fact]
    public void SetDatabase_updates_the_database_and_connection_string_and_keeps_the_session()
    {
        var sessions = new SessionManager();
        sessions.UpdateSession(new ConnectionInfo { SessionId = "web-1", ConnectionString = "Server=localhost;Database=Sales", DatabaseName = "Sales" });
        var before = sessions.GetSession("web-1");

        sessions.SetDatabase("web-1", "master", "Server=localhost;Database=master");

        var after = sessions.GetSession("web-1");
        Assert.Same(before, after);
        Assert.Equal("master", after!.DatabaseName);
        Assert.Equal("Server=localhost;Database=master", after.ConnectionString);
        Assert.True(after.IsConnected);
    }
}
