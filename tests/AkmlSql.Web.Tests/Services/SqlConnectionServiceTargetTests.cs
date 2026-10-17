using System.Data.Common;
using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Review findings on the SQL Server connection service:
/// <list type="bullet">
///   <item>WEB-01 — every change of target moves <see cref="ISqlConnectionService.ConnectionGeneration"/>, so results read
///   under one target are frozen under the next.</item>
///   <item>WEB-02 — a remote server's certificate is validated unless the connection says otherwise.</item>
///   <item>WEB-06 — Disconnect closes the engine's session before it reports success, and says when it could not.</item>
///   <item>WEB-19 — Connect lets the engine open the connection first; a refusal leaves the session as it was.</item>
/// </list>
/// </summary>
public sealed class SqlConnectionServiceTargetTests
{
    private sealed class ScriptedBridge : IEngineBridge
    {
        public BridgeState State { get; set; } = BridgeState.Open;
        public string? ConnectedUrl { get; set; } = "ws://localhost:5181/";
        public string[] EngineCapabilities { get; set; } = { SqlConnectionService.CapabilitySessionDisconnect };
        public string? EngineVersion => null;
        public event Action<BridgeState>? StateChanged;
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }

        public List<(int Type, object Request)> Sent { get; } = new();
        public List<(int Type, object Payload)> Notifications { get; } = new();
        public TestSqlConnectionResponse TestReply { get; set; } = new() { Ok = true };
        public Func<SessionDisconnectResponse> DisconnectReply { get; set; } = () => new SessionDisconnectResponse { Ok = true };

        public void Reopen(string url)
        {
            ConnectedUrl = url;
            State = BridgeState.Open;
            StateChanged?.Invoke(BridgeState.Open);
        }

        public Task<HandshakeResponse> ConnectAsync(EngineConnection c, string? b, string? p, CancellationToken ct) => Task.FromResult(new HandshakeResponse());

        public Task<TResponse> SendAsync<TRequest, TResponse>(int type, TRequest request, CancellationToken ct) where TRequest : class where TResponse : class
        {
            Sent.Add((type, request));
            object reply = type switch
            {
                MessageTypes.TestSqlConnection => TestReply,
                MessageTypes.SessionDisconnect => DisconnectReply(),
                MessageTypes.ChangeDatabase => new ChangeDatabaseResponse { Ok = true, DatabaseName = "Other" },
                _ => throw new InvalidOperationException($"Unexpected request {type}"),
            };
            return Task.FromResult((TResponse)reply);
        }

        public Task SendNotificationAsync<TPayload>(int type, TPayload payload, CancellationToken ct) where TPayload : class
        {
            Notifications.Add((type, payload));
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

    private static ConnectionTarget Remote(bool trust = false) =>
        new(DatabaseKind.SqlServer, "sql.example.com", "Sales", WindowsAuth: false, User: "app", Password: "s3cret", TrustServerCertificate: trust);

    private static DbConnectionStringBuilder Parse(string cs) => new() { ConnectionString = cs };

    private static string SentConnectionString(ScriptedBridge bridge) =>
        Assert.IsType<TestSqlConnectionRequest>(bridge.Sent.Last(s => s.Type == MessageTypes.TestSqlConnection).Request).ConnectionString;

    // ── WEB-19 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_refused_login_leaves_the_session_as_it_was()
    {
        var (sql, bridge) = Create();
        bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "Login failed for user 'app'." };
        int changes = 0;
        sql.StateChanged += () => changes++;

        var result = await sql.ConnectAsync(Remote(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("Login failed for user 'app'.", result.Error);
        Assert.False(sql.IsConnected);
        Assert.Empty(bridge.Notifications);   // no ConnectionChanged: the engine session keeps its connection
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task An_accepted_login_is_tested_before_the_session_switches()
    {
        var (sql, bridge) = Create();

        var result = await sql.ConnectAsync(Remote(), CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.True(sql.IsConnected);
        Assert.Equal(MessageTypes.TestSqlConnection, Assert.Single(bridge.Sent).Type);
        var info = Assert.IsType<ConnectionInfo>(Assert.Single(bridge.Notifications).Payload);
        Assert.Equal(SentConnectionString(bridge), info.ConnectionString);
    }

    // ── WEB-02 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_remote_server_is_encrypted_and_its_certificate_validated_by_default()
    {
        var (sql, bridge) = Create();
        await sql.TestAsync(Remote(), CancellationToken.None);

        var cs = Parse(SentConnectionString(bridge));
        Assert.Equal("True", cs["Encrypt"]!.ToString(), ignoreCase: true);
        Assert.Equal("False", cs["TrustServerCertificate"]!.ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task Trusting_the_certificate_is_the_connections_own_choice()
    {
        var (sql, bridge) = Create();
        await sql.TestAsync(Remote(trust: true), CancellationToken.None);
        Assert.Equal("True", Parse(SentConnectionString(bridge))["TrustServerCertificate"]!.ToString(), ignoreCase: true);

        // The next connection that does not say so is validated again.
        await sql.TestAsync(Remote(), CancellationToken.None);
        Assert.Equal("False", Parse(SentConnectionString(bridge))["TrustServerCertificate"]!.ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task The_legacy_overloads_never_trust_a_remote_certificate()
    {
        var (sql, bridge) = Create();
        await sql.TestAsync("sql.example.com", "Sales", windowsAuth: false, "app", "s3cret", CancellationToken.None);
        Assert.Equal("False", Parse(SentConnectionString(bridge))["TrustServerCertificate"]!.ToString(), ignoreCase: true);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("(local)")]
    [InlineData("127.0.0.1,1433")]
    [InlineData(@".\SQLEXPRESS")]
    public void A_server_on_the_engines_own_machine_keeps_skipping_validation(string server)
    {
        var cs = Parse(SqlConnectionService.BuildConnectionString(server, "master", windowsAuth: true, null, null));
        Assert.Equal("True", cs["TrustServerCertificate"]!.ToString(), ignoreCase: true);
    }

    [Fact]
    public void A_saved_connection_round_trips_its_certificate_choice()
    {
        var saved = new SavedSqlConnection { Server = "sql.example.com", TrustServerCertificate = true };
        var json = System.Text.Json.JsonSerializer.Serialize(saved);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<SavedSqlConnection>(json)!.TrustServerCertificate);
        // A record saved before the option existed validates.
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<SavedSqlConnection>("{\"Server\":\"sql.example.com\"}")!.TrustServerCertificate);
    }

    // ── WEB-01 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_change_of_target_moves_the_generation()
    {
        var (sql, bridge) = Create();
        await sql.ConnectAsync(Remote(), CancellationToken.None);
        var connected = sql.ConnectionGeneration;

        await sql.ConnectAsync(Remote() with { Server = "other.example.com" }, CancellationToken.None);
        var otherServer = sql.ConnectionGeneration;
        Assert.NotEqual(connected, otherServer);

        bridge.EngineCapabilities = new[] { SqlConnectionService.CapabilitySessionDatabase, SqlConnectionService.CapabilitySessionDisconnect };
        await sql.ChangeDatabaseAsync("Other", CancellationToken.None);
        var otherDatabase = sql.ConnectionGeneration;
        Assert.NotEqual(otherServer, otherDatabase);

        sql.ReportCurrentDatabase("Third");             // a script's USE
        var used = sql.ConnectionGeneration;
        Assert.NotEqual(otherDatabase, used);

        bridge.Reopen("ws://other-engine:5181/");      // the bridge came back on another engine
        var otherEngine = sql.ConnectionGeneration;
        Assert.NotEqual(used, otherEngine);

        bridge.Reopen("ws://other-engine:5181/");      // the same engine again: same target
        Assert.Equal(otherEngine, sql.ConnectionGeneration);

        await sql.DisconnectSessionAsync(CancellationToken.None);
        Assert.NotEqual(otherEngine, sql.ConnectionGeneration);
    }

    [Fact]
    public async Task A_refused_connect_does_not_move_the_generation()
    {
        var (sql, bridge) = Create();
        await sql.ConnectAsync(Remote(), CancellationToken.None);
        var before = sql.ConnectionGeneration;

        bridge.TestReply = new TestSqlConnectionResponse { Ok = false, ErrorMessage = "no" };
        await sql.ConnectAsync(Remote() with { Server = "other.example.com" }, CancellationToken.None);

        Assert.Equal(before, sql.ConnectionGeneration);
        Assert.Equal("sql.example.com", sql.Server);
    }

    // ── WEB-06 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Disconnect_waits_for_the_engine_to_close_the_session()
    {
        var (sql, bridge) = Create();
        await sql.ConnectAsync(Remote(), CancellationToken.None);
        bridge.DisconnectReply = () => new SessionDisconnectResponse { Ok = true, RolledBack = true };

        var result = await sql.DisconnectSessionAsync(CancellationToken.None);

        Assert.True(result.Released);
        Assert.True(result.RolledBack);
        Assert.Null(result.Warning);
        Assert.False(sql.IsConnected);
        var request = Assert.IsType<SessionDisconnectRequest>(bridge.Sent.Single(s => s.Type == MessageTypes.SessionDisconnect).Request);
        Assert.Equal(sql.SessionId, request.SessionId);
    }

    [Fact]
    public async Task Disconnect_with_an_unreachable_engine_says_its_session_may_stay_open()
    {
        var (sql, bridge) = Create();
        await sql.ConnectAsync(Remote(), CancellationToken.None);
        bridge.State = BridgeState.Reconnecting;

        var result = await sql.DisconnectSessionAsync(CancellationToken.None);

        Assert.False(result.Released);
        Assert.Equal(SqlConnectionService.DisconnectUnconfirmed, result.Warning);
        Assert.False(sql.IsConnected);
        Assert.DoesNotContain(bridge.Sent, s => s.Type == MessageTypes.SessionDisconnect);
    }

    [Fact]
    public async Task Disconnect_from_an_older_engine_says_it_cannot_close_the_session()
    {
        var (sql, bridge) = Create();
        await sql.ConnectAsync(Remote(), CancellationToken.None);
        bridge.EngineCapabilities = Array.Empty<string>();

        var result = await sql.DisconnectSessionAsync(CancellationToken.None);

        Assert.False(result.Released);
        Assert.Equal(SqlConnectionService.DisconnectNeedsNewerEngine, result.Warning);
        Assert.DoesNotContain(bridge.Sent, s => s.Type == MessageTypes.SessionDisconnect);
    }

    [Fact]
    public async Task Disconnect_reports_a_statement_still_holding_the_connection()
    {
        var (sql, bridge) = Create();
        await sql.ConnectAsync(Remote(), CancellationToken.None);
        bridge.DisconnectReply = () => new SessionDisconnectResponse { Ok = false, ErrorMessage = "A statement is still running." };

        var result = await sql.DisconnectSessionAsync(CancellationToken.None);

        Assert.False(result.Released);
        Assert.Equal("A statement is still running.", result.Warning);
        Assert.False(sql.IsConnected);
    }
}
