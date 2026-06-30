using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Phase 4 (web connection manager). Proves the SECURITY-CRITICAL invariant: TestAsync runs the
/// shared ValidateTarget (identifier + loopback/SSRF) guard BEFORE it sends anything to the engine.
/// TestSqlConnectionHandler opens whatever connection string it is handed with no engine-side host
/// check, so a remote/UNC/Azure target MUST be rejected by the web service before the send — never
/// reaching the bridge. The spy bridge reports State==Open and throws if SendAsync is ever called,
/// so a passing test means the guard short-circuited before the send.
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
    public async Task TestAsync_rejects_a_non_loopback_target_without_touching_the_bridge(string server)
    {
        var svc = Build(out var bridge);

        var (ok, error) = await svc.TestAsync(server, "master", windowsAuth: true, user: null, password: null, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("LOCAL SQL Server", error);
        Assert.False(bridge.SendAttempted, "Guard must reject before any send to the engine.");
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
    public async Task ConnectAsync_rejects_a_non_loopback_target_without_touching_the_bridge()
    {
        // The shared guard must hold on the Connect path too (ConnectionChanged is a notification).
        var svc = Build(out var bridge);

        var (ok, error) = await svc.ConnectAsync("evil.com", "master", windowsAuth: true, user: null, password: null, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("LOCAL SQL Server", error);
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
