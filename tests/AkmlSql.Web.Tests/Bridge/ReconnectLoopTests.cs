using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using MessagePack;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 025 (M3 bridge closure) US3 — exponential-backoff reconnect loop.
/// Contract reference: specs/025-m3-bridge-closure/contracts/backoff-schedule-contract.md.
///
/// Each test wires a queue-based socket factory that hands out a fresh
/// <see cref="FakeBridgeWebSocket"/> per <c>ConnectAsync</c> call (the production
/// pattern — each retry rebuilds the underlying transport from scratch). Jitter is
/// pinned to zero so the deterministic sequence 500 ms / 1 s / 2 s / … can be asserted.
/// </summary>
public sealed class ReconnectLoopTests
{
    // Jitter source pinned to zero — see BackoffSequenceMatchesContract.
    private static TimeSpan ZeroJitter(TimeSpan _, TimeSpan __) => TimeSpan.Zero;

    private static EngineConnection LocalhostConnection() => new()
    {
        Id = "c1", Name = "Local engine", Host = "127.0.0.1", Port = 5081, IsLocalhost = true,
    };

    private static FakeBridgeWebSocket OkHandshakeSocket() => new(frame =>
    {
        var env = MessagePackSerializer.Deserialize<RpcMessage>(frame);
        return MessagePackSerializer.Serialize(new RpcMessage
        {
            MessageType = MessageTypes.HandshakeResponse,
            RequestId = env.RequestId,
            Payload = MessagePackSerializer.Serialize(new HandshakeResponse
            {
                Status = HandshakeStatus.Ok,
                EngineVersion = "1.0.0",
                EngineCapabilities = new[] { "core.format.v1" },
            }),
        });
    });

    private static FakeBridgeWebSocket PinRequiredSocket() => new(frame =>
    {
        var env = MessagePackSerializer.Deserialize<RpcMessage>(frame);
        return MessagePackSerializer.Serialize(new RpcMessage
        {
            MessageType = MessageTypes.HandshakeResponse,
            RequestId = env.RequestId,
            Payload = MessagePackSerializer.Serialize(new HandshakeResponse
            {
                Status = HandshakeStatus.PinRequired,
                ErrorMessage = "Bearer revoked.",
            }),
        });
    });

    private static EngineBridge BuildBridge(
        Queue<FakeBridgeWebSocket> sockets,
        out FakePairingTokenVault vault,
        out FakeConnectionStore connections,
        Func<TimeSpan, TimeSpan, TimeSpan>? jitter = null)
    {
        var v = new FakePairingTokenVault();
        var c = new FakeConnectionStore();
        vault = v;
        connections = c;
        var diag = new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter());
        return new EngineBridge(() => sockets.Dequeue(), diag, v, c, jitter ?? ZeroJitter);
    }

    [Fact]
    public async Task SocketCloseTransitionsToReconnecting()
    {
        var socket1 = OkHandshakeSocket();
        var socket2 = OkHandshakeSocket();
        var queue = new Queue<FakeBridgeWebSocket>(new[] { socket1, socket2 });
        var bridge = BuildBridge(queue, out _, out _);

        var connection = LocalhostConnection();
        await bridge.ConnectAsync(connection, null, null, CancellationToken.None);
        Assert.Equal(BridgeState.Open, bridge.State);

        // Force-close the active socket. Receive loop sees null frame → finally fires →
        // State transitions to Reconnecting and the loop kicks off.
        await socket1.DisposeAsync();

        // Allow the receive loop's finally + Task.Run scheduling to land.
        await WaitForStateAsync(bridge, BridgeState.Reconnecting, TimeSpan.FromSeconds(1));
        Assert.Equal(BridgeState.Reconnecting, bridge.State);

        await bridge.DisconnectAsync();
    }

    [Fact]
    public async Task RetrySucceedsRestoresOpen()
    {
        var socket1 = OkHandshakeSocket();
        var socket2 = OkHandshakeSocket();
        var queue = new Queue<FakeBridgeWebSocket>(new[] { socket1, socket2 });
        var bridge = BuildBridge(queue, out _, out _);

        await bridge.ConnectAsync(LocalhostConnection(), null, null, CancellationToken.None);
        Assert.Equal(BridgeState.Open, bridge.State);

        // Drop the socket — the reconnect loop should pick up socket2 and reach Open.
        await socket1.DisposeAsync();

        await WaitForStateAsync(bridge, BridgeState.Open, TimeSpan.FromSeconds(3));
        Assert.Equal(BridgeState.Open, bridge.State);

        await bridge.DisconnectAsync();
    }

    [Fact]
    public void BackoffSequenceMatchesContract()
    {
        // Deterministic sequence: 500 ms, 1 s, 2 s, 4 s, 8 s, 16 s, 30 s, 30 s, …
        var schedule = new EngineBridge.BackoffSchedule(ZeroJitter);
        var observed = Enumerable.Range(0, 8).Select(_ => schedule.NextDelay().TotalMilliseconds).ToArray();
        Assert.Equal(new double[] { 500, 1_000, 2_000, 4_000, 8_000, 16_000, 30_000, 30_000 }, observed);
    }

    [Fact]
    public void JitterStaysInRange()
    {
        // 1000 iterations with a real random jitter source — every emitted delay lands
        // within ±100 ms of the deterministic base.
        var rng = new Random(1234);
        TimeSpan RandomJitter(TimeSpan min, TimeSpan max)
        {
            var rangeMs = (max - min).TotalMilliseconds;
            return TimeSpan.FromMilliseconds(rng.NextDouble() * rangeMs + min.TotalMilliseconds);
        }

        var schedule = new EngineBridge.BackoffSchedule(RandomJitter);
        double[] expected = { 500, 1_000, 2_000, 4_000, 8_000, 16_000, 30_000, 30_000 };
        for (var iter = 0; iter < 1000; iter++)
        {
            schedule.Reset();
            for (var step = 0; step < expected.Length; step++)
            {
                var delay = schedule.NextDelay().TotalMilliseconds;
                var diff = Math.Abs(delay - expected[step]);
                Assert.True(diff <= 100.001,
                    $"iter {iter} step {step}: delay {delay} ms, base {expected[step]} ms, |diff| {diff} ms");
            }
        }
    }

    [Fact]
    public async Task RevocationTerminatesLoop()
    {
        var openSocket = OkHandshakeSocket();
        var revokedSocket = PinRequiredSocket();
        var queue = new Queue<FakeBridgeWebSocket>(new[] { openSocket, revokedSocket });
        var bridge = BuildBridge(queue, out var vault, out var connections);

        var connection = LocalhostConnection();
        connection.IsLocalhost = false;     // exercise the non-loopback bearer-storage path
        connection.Host = "192.168.1.100";
        connection.BearerTokenWrappedRef = "wrap-ref-1";
        await vault.StoreAsync(connection.Id, "real-bearer", DateTimeOffset.UtcNow.AddDays(90));
        await connections.AddAsync(connection);

        await bridge.ConnectAsync(connection, "real-bearer", null, CancellationToken.None);
        Assert.Equal(BridgeState.Open, bridge.State);

        // Drop the socket — retry will land on revokedSocket and the engine says PinRequired.
        await openSocket.DisposeAsync();

        await WaitForStateAsync(bridge, BridgeState.Failed, TimeSpan.FromSeconds(3));
        Assert.Equal(BridgeState.Failed, bridge.State);
        Assert.Contains(connection.Id, vault.Removed);
        var updated = await connections.GetAsync(connection.Id);
        Assert.Null(updated!.BearerTokenWrappedRef);

        await bridge.DisconnectAsync();
    }

    [Fact]
    public async Task DisconnectAsyncBypassesRetry()
    {
        var socket1 = OkHandshakeSocket();
        // socket2 won't be dequeued because we'll bail out via DisconnectAsync.
        var queue = new Queue<FakeBridgeWebSocket>(new[] { socket1, OkHandshakeSocket() });
        // Make the backoff long enough to stay in Reconnecting for the duration of the test.
        TimeSpan LongJitter(TimeSpan _, TimeSpan __) => TimeSpan.FromSeconds(5);
        var bridge = BuildBridge(queue, out _, out _, LongJitter);

        await bridge.ConnectAsync(LocalhostConnection(), null, null, CancellationToken.None);
        await socket1.DisposeAsync();
        await WaitForStateAsync(bridge, BridgeState.Reconnecting, TimeSpan.FromSeconds(1));

        // Mid-Reconnecting: user disconnects. Expected end state = Disconnected.
        await bridge.DisconnectAsync();
        Assert.Equal(BridgeState.Disconnected, bridge.State);
    }

    [Fact]
    public async Task InBrowserWorkSurvivesReconnect()
    {
        // FR-015: the formatter / analyser run in-browser even while Reconnecting.
        // This test asserts the closed-bridge path of CompletionService — which is the
        // bridge-routed surface — returns an empty result rather than throwing,
        // matching the offline contract used by FormatterService / AnalyserService.
        var socket1 = OkHandshakeSocket();
        var queue = new Queue<FakeBridgeWebSocket>(new[] { socket1, OkHandshakeSocket() });
        var bridge = BuildBridge(queue, out _, out _, jitter: (_, __) => TimeSpan.FromSeconds(5));

        await bridge.ConnectAsync(LocalhostConnection(), null, null, CancellationToken.None);
        await socket1.DisposeAsync();
        await WaitForStateAsync(bridge, BridgeState.Reconnecting, TimeSpan.FromSeconds(1));

        var completion = new CompletionService(bridge);
        var response = await completion.CompleteAsync(new CompletionRequest(), CancellationToken.None);
        Assert.NotNull(response);
        Assert.Empty(response.Items ?? Array.Empty<CompletionItem>());

        await bridge.DisconnectAsync();
    }

    private static async Task WaitForStateAsync(IEngineBridge bridge, BridgeState target, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (bridge.State == target) return;
            await Task.Delay(20);
        }
    }
}

// ── Test doubles ────────────────────────────────────────────────────────────────

internal sealed class FakePairingTokenVault : IPairingTokenVault
{
    private readonly Dictionary<string, string> _store = new();
    public List<string> Removed { get; } = new();

    public Task<string> RetrieveAsync(string connectionId) =>
        _store.TryGetValue(connectionId, out var token)
            ? Task.FromResult(token)
            : Task.FromException<string>(new InvalidOperationException("missing"));

    public Task<string> StoreAsync(string connectionId, string plainToken, DateTimeOffset ttlExpiresAt)
    {
        _store[connectionId] = plainToken;
        return Task.FromResult("wrap-ref-" + connectionId);
    }

    public Task RemoveAsync(string connectionId)
    {
        Removed.Add(connectionId);
        _store.Remove(connectionId);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string connectionId) =>
        Task.FromResult(_store.ContainsKey(connectionId));
}

internal sealed class FakeConnectionStore : IConnectionStore
{
    private readonly Dictionary<string, EngineConnection> _conns = new();
    private string? _activeId;

    public Task<IReadOnlyList<EngineConnection>> ListAsync() =>
        Task.FromResult<IReadOnlyList<EngineConnection>>(_conns.Values.OrderBy(c => c.Name).ToList());

    public Task<EngineConnection?> GetAsync(string id) =>
        Task.FromResult(_conns.TryGetValue(id, out var c) ? c : null);

    public Task AddAsync(EngineConnection connection)
    {
        if (_conns.ContainsKey(connection.Id))
            throw new InvalidOperationException($"Connection '{connection.Id}' already exists.");
        _conns[connection.Id] = connection;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(EngineConnection connection)
    {
        if (!_conns.ContainsKey(connection.Id))
            throw new InvalidOperationException($"Connection '{connection.Id}' does not exist.");
        _conns[connection.Id] = connection;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string id)
    {
        _conns.Remove(id);
        if (_activeId == id) _activeId = null;
        return Task.CompletedTask;
    }

    public Task<string?> GetActiveIdAsync() => Task.FromResult(_activeId);
    public Task SetActiveIdAsync(string? id) { _activeId = id; return Task.CompletedTask; }
}
