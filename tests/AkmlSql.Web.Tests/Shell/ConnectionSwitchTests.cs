using AkmlSql.Core;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 042 (task T112, FR-007, FR-008, SC-002). Switching between a SQL Server and a PostgreSQL connection keeps one engine
/// session, changes the kind and everything that hangs on it each time, and leaves nothing of the other kind behind. The status
/// bar, the editor's language and the schema explorer all read these values, so they follow without a reload. The engine is a
/// scripted bridge; the 20-switch timing is measured in the browser tests.
/// </summary>
public sealed class ConnectionSwitchTests
{
    private sealed class ScriptedBridge : IEngineBridge
    {
        public BridgeState State { get; set; } = BridgeState.Open;
        public string[] EngineCapabilities { get; set; } = { SqlConnectionService.CapabilityPostgreSql };
        public string? EngineVersion => null;
        public event Action<BridgeState>? StateChanged { add { } remove { } }
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }

        public List<ConnectionInfo> ConnectionChanges { get; } = new();
        public TestSqlConnectionResponse TestReply { get; set; } = new() { Ok = true, ServerVersion = "PostgreSQL 16.4" };

        public Task<HandshakeResponse> ConnectAsync(EngineConnection c, string? b, string? p, CancellationToken ct) => Task.FromResult(new HandshakeResponse());

        public Task<TResponse> SendAsync<TRequest, TResponse>(int type, TRequest request, CancellationToken ct) where TRequest : class where TResponse : class
        {
            object reply = type switch
            {
                MessageTypes.TestSqlConnection => TestReply,
                _ => throw new InvalidOperationException($"Unexpected request {type}"),
            };
            return Task.FromResult((TResponse)reply);
        }

        public Task SendNotificationAsync<TPayload>(int type, TPayload payload, CancellationToken ct) where TPayload : class
        {
            if (type == MessageTypes.ConnectionChanged) ConnectionChanges.Add((ConnectionInfo)(object)payload);
            return Task.CompletedTask;
        }

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

    private static (SqlConnectionService Sql, ScriptedBridge Bridge) Create()
    {
        var bridge = new ScriptedBridge();
        return (new SqlConnectionService(bridge, new NoDiagnostics()), bridge);
    }

    private static ConnectionTarget Postgres() =>
        new(DatabaseKind.PostgreSql, "pg1", "northwind", User: "app", Password: "s3cret", Port: 5433);

    private static Task<ConnectResult> ConnectSqlServer(SqlConnectionService sql) =>
        sql.ConnectAsync(new ConnectionTarget(DatabaseKind.SqlServer, "localhost", "Northwind", WindowsAuth: true), default);

    [Fact]
    public async Task Switching_back_and_forth_keeps_one_session_and_follows_the_kind_every_time()
    {
        var (sql, bridge) = Create();
        var changes = 0;
        sql.StateChanged += () => changes++;

        for (var round = 0; round < 10; round++)
        {
            Assert.True((await ConnectSqlServer(sql)).Ok);
            Assert.Equal(DatabaseKind.SqlServer, sql.Kind);
            Assert.Equal("localhost", sql.Server);

            var pg = await sql.ConnectAsync(Postgres(), default);
            Assert.True(pg.Ok, pg.Error);
            Assert.Equal(DatabaseKind.PostgreSql, sql.Kind);
            Assert.Equal("pg1:5433", sql.Server);
            Assert.Equal("16.4", sql.ServerVersionText);
        }

        Assert.Equal(20, bridge.ConnectionChanges.Count);
        Assert.All(bridge.ConnectionChanges, c => Assert.Equal(sql.SessionId, c.SessionId));
        Assert.Equal(
            Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? (int)DatabaseKind.SqlServer : (int)DatabaseKind.PostgreSql),
            bridge.ConnectionChanges.Select(c => c.Kind));
        Assert.True(changes >= 20, "every switch tells the status bar, the editor and the explorer");
    }

    [Fact]
    public async Task Going_back_to_SQL_Server_leaves_nothing_of_PostgreSQL_behind()
    {
        var (sql, _) = Create();
        await sql.ConnectAsync(Postgres(), default);
        sql.ReportTransactionState(TransactionState.Open);
        Assert.Equal(TransactionState.Open, sql.TransactionState);
        Assert.Equal("pg://pg1:5433", sql.SchemaIdentity);

        Assert.True((await ConnectSqlServer(sql)).Ok);

        Assert.Equal(DatabaseKind.SqlServer, sql.Kind);
        Assert.Null(sql.ServerVersionText);
        Assert.Equal(TransactionState.None, sql.TransactionState);
        Assert.Equal("localhost", sql.SchemaIdentity);
        Assert.Null(sql.Login);
        Assert.True(sql.WindowsAuth);
    }

    [Fact]
    public async Task Going_to_PostgreSQL_leaves_nothing_of_SQL_Server_behind()
    {
        var (sql, _) = Create();
        await ConnectSqlServer(sql);

        await sql.ConnectAsync(Postgres(), default);

        Assert.Equal("pg1:5433", sql.Server);
        Assert.Equal("northwind", sql.Database);
        Assert.Equal("app", sql.Login);
        Assert.False(sql.WindowsAuth);
        Assert.Equal("pg://pg1:5433", sql.SchemaIdentity);
        Assert.Equal(TransactionState.None, sql.TransactionState);
    }

    [Fact]
    public async Task A_switch_that_fails_leaves_the_current_session_exactly_as_it_was()
    {
        var (sql, bridge) = Create();
        await ConnectSqlServer(sql);
        var before = bridge.ConnectionChanges.Count;
        bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Could not reach pg1:5433: connection refused." };

        var result = await sql.ConnectAsync(Postgres(), default);

        Assert.False(result.Ok);
        Assert.Equal(DatabaseKind.SqlServer, sql.Kind);
        Assert.Equal("localhost", sql.Server);
        Assert.True(sql.IsConnected);
        Assert.Equal(before, bridge.ConnectionChanges.Count);
    }

    [Fact]
    public async Task An_engine_without_PostgreSQL_refuses_the_switch_and_changes_nothing()
    {
        var (sql, bridge) = Create();
        await ConnectSqlServer(sql);
        bridge.EngineCapabilities = Array.Empty<string>();

        var result = await sql.ConnectAsync(Postgres(), default);

        Assert.False(result.Ok);
        Assert.Equal(DatabaseKind.SqlServer, sql.Kind);
        Assert.Single(bridge.ConnectionChanges);
    }

    [Fact]
    public void The_schema_cache_names_of_the_two_kinds_can_never_be_equal()
    {
        // A SQL Server called "pg://pg1:5433" cannot be reached by the SQL Server guard, and no SQL Server name starts with pg://.
        Assert.StartsWith("pg://", PgConnectionString.ServerKey("pg1", 5433));
        Assert.NotEqual("pg1:5433", PgConnectionString.ServerKey("pg1", 5433));
    }
}
