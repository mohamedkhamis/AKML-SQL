using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 041 (T182, research R25–R28) — the web side of the database selector: it lists and switches only when the
/// engine advertises <c>session.database.v1</c>, runs the same target guard as connect first, keeps the connection's
/// authentication and login, and follows the database a script's <c>USE</c> moved to.
/// </summary>
public sealed class SqlDatabaseSwitchTests
{
    private sealed class ScriptedBridge : IEngineBridge
    {
        public BridgeState State { get; set; } = BridgeState.Open;
        public string[] EngineCapabilities { get; set; } = { SqlConnectionService.CapabilitySessionDatabase };
        public string? EngineVersion => null;
        public event Action<BridgeState>? StateChanged { add { } remove { } }
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }

        public List<(int Type, object Request)> Sent { get; } = new();
        public ListDatabasesResponse ListReply { get; set; } = new() { Ok = true, Databases = new List<string> { "master", "Sales" } };
        public ChangeDatabaseResponse ChangeReply { get; set; } = new() { Ok = true, DatabaseName = "master" };

        public Task<HandshakeResponse> ConnectAsync(EngineConnection c, string? b, string? p, CancellationToken ct) => Task.FromResult(new HandshakeResponse());

        public Task<TResponse> SendAsync<TRequest, TResponse>(int type, TRequest request, CancellationToken ct) where TRequest : class where TResponse : class
        {
            Sent.Add((type, request));
            object reply = type switch
            {
                MessageTypes.ListDatabases => ListReply,
                MessageTypes.ChangeDatabase => ChangeReply,
                MessageTypes.TestSqlConnection => new TestSqlConnectionResponse { Ok = true },
                _ => throw new InvalidOperationException($"Unexpected request {type}"),
            };
            return Task.FromResult((TResponse)reply);
        }

        public Task SendNotificationAsync<TPayload>(int type, TPayload payload, CancellationToken ct) where TPayload : class => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private sealed class NoDiagnostics : IDiagnosticsRingBuffer
    {
        public void Log(DiagnosticLevel level, string source, string message, object? data = null) { }
        public IReadOnlyList<DiagnosticEntry> Snapshot() => Array.Empty<DiagnosticEntry>();
        public void Clear() { }
        public Task FlushAsync() => Task.CompletedTask;
        public Task RestoreAsync() => Task.CompletedTask;
    }

    private static async Task<(SqlConnectionService Sql, ScriptedBridge Bridge)> ConnectedAsync(bool windowsAuth = true, string server = "localhost")
    {
        var bridge = new ScriptedBridge();
        var sql = new SqlConnectionService(bridge, new NoDiagnostics());
        var (ok, error) = await sql.ConnectAsync(server, "Sales", windowsAuth, windowsAuth ? null : "report_user", windowsAuth ? null : "secret", CancellationToken.None);
        Assert.True(ok, error);
        bridge.Sent.Clear();
        return (sql, bridge);
    }

    [Fact]
    public async Task The_list_comes_from_the_session_not_from_a_resent_password()
    {
        var (sql, bridge) = await ConnectedAsync(windowsAuth: false, server: "remote-sql");

        var (ok, databases, _) = await sql.ListDatabasesForSessionAsync(CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(new[] { "master", "Sales" }, databases);
        var request = Assert.IsType<ListDatabasesRequest>(Assert.Single(bridge.Sent).Request);
        Assert.Equal(sql.SessionId, request.SessionId);
        Assert.Equal(string.Empty, request.ConnectionString);
    }

    [Fact]
    public async Task Switching_sends_217_and_follows_the_servers_answer()
    {
        var (sql, bridge) = await ConnectedAsync();
        int changed = 0;
        sql.StateChanged += () => changed++;

        var (ok, error, _) = await sql.ChangeDatabaseAsync("master", CancellationToken.None);

        Assert.True(ok, error);
        var (type, request) = Assert.Single(bridge.Sent);
        Assert.Equal(MessageTypes.ChangeDatabase, type);
        Assert.Equal("master", ((ChangeDatabaseRequest)request).DatabaseName);
        Assert.Equal("master", sql.Database);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task A_refused_switch_keeps_the_database_and_says_why()
    {
        var (sql, bridge) = await ConnectedAsync();
        bridge.ChangeReply = new ChangeDatabaseResponse { Ok = false, ErrorMessage = "The server principal is not able to access the database." };

        var (ok, error, _) = await sql.ChangeDatabaseAsync("HR", CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("not able to access", error);
        Assert.Equal("Sales", sql.Database);
    }

    [Theory]
    [InlineData("master;Integrated Security=true")]
    [InlineData("")]
    public async Task The_guard_runs_before_anything_is_sent(string database)
    {
        var (sql, bridge) = await ConnectedAsync();

        var (ok, _, _) = await sql.ChangeDatabaseAsync(database, CancellationToken.None);

        Assert.False(ok);
        Assert.Empty(bridge.Sent);
    }

    [Fact]
    public async Task Without_the_capability_nothing_is_sent()
    {
        var (sql, bridge) = await ConnectedAsync();
        bridge.EngineCapabilities = Array.Empty<string>();

        Assert.False(sql.CanChangeDatabase);
        Assert.False((await sql.ChangeDatabaseAsync("master", CancellationToken.None)).Ok);
        Assert.False((await sql.ListDatabasesForSessionAsync(CancellationToken.None)).Ok);
        Assert.Empty(bridge.Sent);
    }

    [Fact]
    public async Task A_scripts_USE_moves_the_reported_database_and_raises_StateChanged_only_on_a_change()
    {
        var (sql, _) = await ConnectedAsync();
        int changed = 0;
        sql.StateChanged += () => changed++;

        sql.ReportCurrentDatabase("Sales");
        sql.ReportCurrentDatabase(null);
        sql.ReportCurrentDatabase("master");
        sql.ReportCurrentDatabase("master");

        Assert.Equal("master", sql.Database);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task The_login_is_kept_the_password_never_is()
    {
        var (sql, _) = await ConnectedAsync(windowsAuth: false, server: "remote-sql");

        Assert.False(sql.WindowsAuth);
        Assert.Equal("report_user", sql.Login);

        await sql.DisconnectAsync();
        Assert.Null(sql.Login);
    }
}
