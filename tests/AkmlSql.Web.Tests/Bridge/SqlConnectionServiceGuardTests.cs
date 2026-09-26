using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Phase 4 (web connection manager). Proves the SECURITY-CRITICAL invariant: TestAsync runs the
/// shared ValidateTarget guard BEFORE it sends anything to the engine. A target that would make the
/// engine sign in as ITSELF somewhere else -- Windows authentication to a remote host, or any
/// named-pipe/UNC address -- is rejected before the send and never reaches the bridge. (The engine
/// enforces the same rule itself: BridgeSqlTargetGuard.) A remote server with SQL Server
/// authentication, which only forwards the login the user typed, is allowed through.
/// </summary>
public sealed class SqlConnectionServiceGuardTests
{
    private static ISqlConnectionService Build(out OpenSpyBridge bridge)
    {
        bridge = new OpenSpyBridge();
        return new SqlConnectionService(bridge, new NoopDiagnostics());
    }

    [Theory]
    [InlineData("evil.com")]
    [InlineData("10.0.0.5")]
    [InlineData("db.database.windows.net")]
    [InlineData("\\\\fileserver\\share")]
    [InlineData("tcp:remote-host,1433")]
    [InlineData("127.0.0.1.attacker.com")]
    [InlineData("::ffff:10.0.0.5")]      // IPv4-mapped IPv6: parses, but IsLoopback(::1-only) is false ⇒ blocked
    [InlineData("[::ffff:10.0.0.5]")]    // bracketed form: brackets stripped, same host, still blocked
    public async Task TestAsync_rejects_windows_auth_to_a_remote_target_without_touching_the_bridge(string server)
    {
        var svc = Build(out var bridge);

        var (ok, error) = await svc.TestAsync(server, "master", windowsAuth: true, user: null, password: null, CancellationToken.None);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.False(bridge.SendAttempted, "Guard must reject before any send to the engine.");
    }

    [Theory]
    [InlineData("evil.com")]
    [InlineData("10.0.0.5")]
    [InlineData("162.220.54.191,1433")]
    [InlineData(@"tcp:remote-host\SQLEXPRESS")]
    [InlineData("db.database.windows.net")]
    public async Task TestAsync_with_sql_auth_allows_a_remote_server(string server)
    {
        // The engine forwards the login the user typed; no identity of its own is involved.
        var svc = Build(out var bridge);
        bridge.NextTestResult = new TestSqlConnectionResponse { Ok = true };

        var (ok, error) = await svc.TestAsync(server, "master", windowsAuth: false, user: "sa", password: "pw", CancellationToken.None);

        Assert.True(ok, error);
        Assert.True(bridge.SendAttempted);
    }

    [Fact]
    public async Task TestAsync_explains_why_windows_auth_is_refused_for_a_remote_server()
    {
        var svc = Build(out _);

        var (_, error) = await svc.TestAsync("10.0.0.5", "master", windowsAuth: true, user: null, password: null, CancellationToken.None);

        Assert.Contains("Windows authentication only works for a SQL Server on the engine's own machine", error);
        Assert.Contains("SQL Server authentication", error);
    }

    [Theory]
    [InlineData(@"\\fileserver\share")]
    [InlineData(@"np:\\remote\pipe\sql\query")]
    [InlineData("lpc:remote-host")]
    public async Task A_named_pipe_or_UNC_target_is_refused_even_with_sql_auth(string server)
    {
        // A named pipe is SMB: opening it authenticates as the engine's account whatever the SQL login.
        var svc = Build(out var bridge);

        var (ok, error) = await svc.TestAsync(server, "master", windowsAuth: false, user: "sa", password: "pw", CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("over TCP", error);
        Assert.False(bridge.SendAttempted);
    }

    [Theory]
    [InlineData("server;Integrated Security=false")]
    [InlineData("master\"")]
    public async Task TestAsync_rejects_identifier_metacharacters_without_touching_the_bridge(string server)
    {
        var svc = Build(out var bridge);

        var (ok, error) = await svc.TestAsync(server, "master", windowsAuth: true, user: null, password: null, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("invalid characters", error);
        Assert.False(bridge.SendAttempted);
    }

    [Fact]
    public async Task ConnectAsync_rejects_windows_auth_to_a_remote_target_without_touching_the_bridge()
    {
        // The shared guard must hold on the Connect path too (ConnectionChanged is a notification).
        var svc = Build(out var bridge);

        var (ok, error) = await svc.ConnectAsync("evil.com", "master", windowsAuth: true, user: null, password: null, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("Windows authentication", error);
        Assert.False(bridge.NotifyAttempted, "Guard must reject before sending ConnectionChanged.");
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public async Task TestAsync_with_a_loopback_target_passes_the_guard_and_reaches_the_bridge()
    {
        // localhost is accepted: the guard passes, so the service proceeds to SendAsync (which the
        // spy answers Ok). This confirms the guard does not over-block legitimate loopback targets.
        var svc = Build(out var bridge);
        bridge.NextTestResult = new TestSqlConnectionResponse { Ok = true };

        var (ok, error) = await svc.TestAsync("localhost", "master", windowsAuth: true, user: null, password: null, CancellationToken.None);

        Assert.True(ok);
        Assert.Null(error);
        Assert.True(bridge.SendAttempted);
    }

    // ── Test doubles ────────────────────────────────────────────────────────────────────────
    private sealed class OpenSpyBridge : IEngineBridge
    {
        public bool SendAttempted { get; private set; }
        public bool NotifyAttempted { get; private set; }
        public TestSqlConnectionResponse? NextTestResult { get; set; }

        public BridgeState State => BridgeState.Open;   // open, so the guard — not bridge state — is what blocks
        public event Action<BridgeState>? StateChanged { add { } remove { } }
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }
        public string[] EngineCapabilities => Array.Empty<string>();
        public string? EngineVersion => null;

        public Task<HandshakeResponse> ConnectAsync(EngineConnection c, string? b, string? p, CancellationToken ct) =>
            Task.FromResult(new HandshakeResponse());

        public Task<TResponse> SendAsync<TRequest, TResponse>(int t, TRequest r, CancellationToken ct)
            where TRequest : class where TResponse : class
        {
            SendAttempted = true;
            object resp = NextTestResult ?? new TestSqlConnectionResponse { Ok = false, ErrorMessage = "no result" };
            return Task.FromResult((TResponse)resp);
        }

        public Task SendNotificationAsync<TPayload>(int t, TPayload p, CancellationToken ct) where TPayload : class
        {
            NotifyAttempted = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private sealed class NoopDiagnostics : IDiagnosticsRingBuffer
    {
        public void Log(DiagnosticLevel level, string source, string message, object? data = null) { }
        public System.Collections.Generic.IReadOnlyList<DiagnosticEntry> Snapshot() => Array.Empty<DiagnosticEntry>();
        public void Clear() { }
        public Task FlushAsync() => Task.CompletedTask;
        public Task RestoreAsync() => Task.CompletedTask;
    }
}
