using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using MessagePack;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests;

/// <summary>
/// Spec 025 (M3 bridge closure) FR-027. Covers the engine-host composition of the
/// optional <see cref="WebSocketTransport"/> alongside the existing named-pipe
/// transport. The full RunAsync integration is exercised end-to-end in the deferred
/// <c>tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs</c> (US5); the unit tests here
/// pin the <see cref="EngineHost.BuildWebSocketTransport"/> mapping + the
/// disabled/absent no-op contract.
/// </summary>
public sealed class EngineHostTests
{
    /// <summary>
    /// FR-027 / <c>BridgeDisabledFlagStartsPipeOnly</c>: when the bridge config exists
    /// but <see cref="BridgeOptions.Enabled"/> is <c>false</c>, no WebSocket transport
    /// is built — the engine host runs the named pipe only, identical to the
    /// IDE-plugin-only deployment.
    /// </summary>
    [Fact]
    public void BuildWebSocketTransport_returns_null_when_disabled()
    {
        var bridge = new BridgeOptions { Enabled = false };

        var ws = EngineHost.BuildWebSocketTransport(bridge);

        Assert.Null(ws);
    }

    /// <summary>
    /// FR-027 / <c>NoBridgeSectionStartsPipeOnly</c>: when the bridge config is null
    /// (absent from <c>config.json</c>), no WebSocket transport is built — the engine
    /// host runs the named pipe only.
    /// </summary>
    [Fact]
    public void BuildWebSocketTransport_returns_null_when_section_absent()
    {
        var ws = EngineHost.BuildWebSocketTransport(null!);

        Assert.Null(ws);
    }

    /// <summary>
    /// FR-027 / <c>DualTransportCompositionRoutesViaSameRouter</c> (unit-mapping
    /// portion): when <see cref="BridgeOptions.Enabled"/> is <c>true</c>, a
    /// <see cref="WebSocketTransport"/> is built and the <see cref="BridgeOptions"/>
    /// fields map 1:1 onto <see cref="WebSocketTransportOptions"/>. The on-the-wire
    /// shared-router assertion is the responsibility of the US5 E2E suite.
    /// </summary>
    [Fact]
    public async Task BuildWebSocketTransport_constructs_transport_when_enabled_localhost()
    {
        var bridge = new BridgeOptions
        {
            Enabled = true,
            BindAddress = "127.0.0.1",
            Port = 53000 + Random.Shared.Next(0, 1000),
            TlsCertPath = string.Empty,
            TokenStorePath = string.Empty,
            TokenTtlDays = 90,
        };

        var ws = EngineHost.BuildWebSocketTransport(bridge);

        Assert.NotNull(ws);
        await ws!.DisposeAsync();
    }

    /// <summary>
    /// FR-027 + FR-013a: enabling the bridge on a non-loopback address without a
    /// <see cref="BridgeOptions.TlsCertPath"/> MUST refuse at <see cref="WebSocketTransport"/>
    /// construction (the existing FR-013a guard in <c>WebSocketTransport</c>'s constructor).
    /// </summary>
    [Fact]
    public void BuildWebSocketTransport_refuses_lan_without_cert()
    {
        var bridge = new BridgeOptions
        {
            Enabled = true,
            BindAddress = "0.0.0.0",
            Port = 53100,
            TlsCertPath = string.Empty,
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => EngineHost.BuildWebSocketTransport(bridge));
        Assert.Contains("TlsCertPath", ex.Message);
        Assert.Contains("FR-013a", ex.Message);
    }

    /// <summary>
    /// FR-027 end-to-end localhost smoke: build the transport, start it, send a Ping
    /// over a real <see cref="ClientWebSocket"/>, observe the same response we'd get
    /// from the named-pipe path against the same router. This verifies the routing
    /// shape — the engine-host wire-up code in <see cref="EngineHost.RunAsync"/> uses
    /// exactly this pattern to attach the WS transport to the composition router.
    /// </summary>
    [Fact]
    public async Task DualTransportComposition_routes_via_same_handler()
    {
        var bridge = new BridgeOptions
        {
            Enabled = true,
            BindAddress = "127.0.0.1",
            Port = 53200 + Random.Shared.Next(0, 1000),
        };

        await using var ws = EngineHost.BuildWebSocketTransport(bridge)!;
        var pingCount = 0;
        ws.RequestReceived += (msg, _) =>
        {
            Interlocked.Increment(ref pingCount);
            return Task.FromResult<RpcMessage?>(new RpcMessage
            {
                MessageType = msg.MessageType + 1,
                RequestId = msg.RequestId,
                Payload = msg.Payload,
            });
        };

        await ws.StartAsync(CancellationToken.None);

        // One Ping over WebSocket
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{bridge.Port}/"), CancellationToken.None);

        var request = new RpcMessage { MessageType = 100, RequestId = 1, Payload = new byte[] { 1, 2, 3 } };
        var payload = MessagePackSerializer.Serialize(request);
        await client.SendAsync(new ArraySegment<byte>(payload),
            WebSocketMessageType.Binary, true, CancellationToken.None);

        using var ms = new System.IO.MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var response = MessagePackSerializer.Deserialize<RpcMessage>(ms.ToArray());

        Assert.Equal(1, pingCount);
        Assert.Equal(101, response.MessageType);
        Assert.Equal(1, response.RequestId);
    }

    /// <summary>
    /// Spec 026 (M4 closure) C1 / H1 / FR-013c / SC-010: when the transport requires pairing
    /// (LAN mode), a connection that sends a non-handshake RPC BEFORE completing a successful
    /// handshake is rejected with an <see cref="MessageTypes.Error"/> envelope and the router never
    /// sees the frame; after an Ok handshake the same RPC is routed normally. Runs on loopback with
    /// <see cref="WebSocketTransportOptions.RequirePairingToken"/> forced true so the gate is
    /// exercised without TLS/admin (the ctor only demands a cert for a non-loopback BindAddress).
    /// This is the negative test the original bypass slipped through for lack of.
    /// </summary>
    [Fact]
    public async Task LanGate_rejects_rpc_before_handshake_then_allows_after_ok()
    {
        var port = 53400 + Random.Shared.Next(0, 1000);
        await using var ws = new WebSocketTransport(new WebSocketTransportOptions
        {
            BindAddress = "127.0.0.1",
            Port = port,
            RequirePairingToken = true,   // gate active; loopback => the ctor needs no TLS cert
        });

        var routed = new System.Collections.Concurrent.ConcurrentBag<int>();
        ws.RequestReceived += (msg, _) =>
        {
            routed.Add(msg.MessageType);
            if (msg.MessageType == MessageTypes.HandshakeRequest)
            {
                return Task.FromResult<RpcMessage?>(new RpcMessage
                {
                    MessageType = MessageTypes.HandshakeResponse,
                    RequestId = msg.RequestId,
                    Payload = MessagePackSerializer.Serialize(
                        new HandshakeResponse { Status = HandshakeStatus.Ok }),
                });
            }
            return Task.FromResult<RpcMessage?>(new RpcMessage
            {
                MessageType = msg.MessageType + 1,
                RequestId = msg.RequestId,
                Payload = msg.Payload,
            });
        };

        await ws.StartAsync(CancellationToken.None);
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        // 1. Pre-handshake data RPC -> rejected with an Error envelope; the handler never sees it.
        var pre = await SendReceiveAsync(client,
            new RpcMessage { MessageType = 100, RequestId = 1, Payload = new byte[] { 1 } });
        Assert.Equal(MessageTypes.Error, pre.MessageType);
        Assert.DoesNotContain(100, routed);

        // 2. Complete the handshake -> Ok flips the per-connection gate open.
        var hs = await SendReceiveAsync(client, new RpcMessage
        {
            MessageType = MessageTypes.HandshakeRequest,
            RequestId = 2,
            Payload = MessagePackSerializer.Serialize(new HandshakeRequest()),
        });
        Assert.Equal(MessageTypes.HandshakeResponse, hs.MessageType);

        // 3. The same data RPC, now authenticated -> routed and answered.
        var post = await SendReceiveAsync(client,
            new RpcMessage { MessageType = 100, RequestId = 3, Payload = new byte[] { 1, 2, 3 } });
        Assert.Equal(101, post.MessageType);
        Assert.Equal(3, post.RequestId);
        Assert.Contains(100, routed);
    }

    /// <summary>
    /// Spec 026 (M4 closure) C1: control case — a loopback transport with
    /// <see cref="WebSocketTransportOptions.RequirePairingToken"/> false (the localhost default)
    /// must NOT gate: a non-handshake RPC sent as the first frame is routed immediately. Guards
    /// against the gate regressing IDE-plugin / localhost behaviour.
    /// </summary>
    [Fact]
    public async Task LoopbackTransport_does_not_gate_when_pairing_not_required()
    {
        var port = 53600 + Random.Shared.Next(0, 1000);
        await using var ws = new WebSocketTransport(new WebSocketTransportOptions
        {
            BindAddress = "127.0.0.1",
            Port = port,
            RequirePairingToken = false,
        });

        ws.RequestReceived += (msg, _) => Task.FromResult<RpcMessage?>(new RpcMessage
        {
            MessageType = msg.MessageType + 1,
            RequestId = msg.RequestId,
            Payload = msg.Payload,
        });

        await ws.StartAsync(CancellationToken.None);
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        var resp = await SendReceiveAsync(client,
            new RpcMessage { MessageType = 100, RequestId = 7, Payload = new byte[] { 9 } });

        Assert.Equal(101, resp.MessageType);
        Assert.Equal(7, resp.RequestId);
    }

    /// <summary>Sends one MessagePack RpcMessage frame and reads the single response frame.</summary>
    private static async Task<RpcMessage> SendReceiveAsync(ClientWebSocket client, RpcMessage request)
    {
        var payload = MessagePackSerializer.Serialize(request);
        await client.SendAsync(new ArraySegment<byte>(payload),
            WebSocketMessageType.Binary, true, CancellationToken.None);

        using var ms = new System.IO.MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return MessagePackSerializer.Deserialize<RpcMessage>(ms.ToArray());
    }

    // --- Spec 026 (M4 closure) FR-013a..e / SC-010: LAN auth composition matrix ---

    /// <summary>
    /// FR-013c / FR-013d / SC-010: in LAN mode the composed handshake handler refuses a wrong PIN
    /// (<see cref="HandshakeStatus.PinInvalid"/>, no bearer), accepts the right PIN
    /// (<see cref="HandshakeStatus.Ok"/> + a minted bearer), and the PIN is single-use (a second
    /// handshake with the same PIN is refused).
    /// </summary>
    [Fact]
    public async Task BuildBridgeAuth_lan_enforces_pin_single_use()
    {
        var tokenPath = Path.Combine(Path.GetTempPath(), $"akml-tokens-{Guid.NewGuid():N}.json");
        try
        {
            var bridge = new BridgeOptions
            {
                Enabled = true,
                BindAddress = "0.0.0.0",
                Port = 47291,
                TokenStorePath = tokenPath,
                TokenTtlDays = 90,
            };

            var auth = EngineHost.BuildBridgeAuth(bridge);
            Assert.NotNull(auth.Pairing);           // LAN mode constructs a live pairing service
            var ctx = MinimalContext();
            var validPin = auth.Pairing!.CurrentPin;
            var wrongPin = validPin == "000000" ? "111111" : "000000";

            // Wrong PIN -> PinInvalid, no bearer.
            var wrong = await auth.Handshake.HandleAsync(MakeHandshake(pin: wrongPin), ctx, default);
            Assert.Equal(HandshakeStatus.PinInvalid, wrong.Status);
            Assert.True(string.IsNullOrEmpty(wrong.NewBearerToken));

            // Correct PIN -> Ok + minted bearer.
            var ok = await auth.Handshake.HandleAsync(MakeHandshake(pin: validPin), ctx, default);
            Assert.Equal(HandshakeStatus.Ok, ok.Status);
            Assert.False(string.IsNullOrEmpty(ok.NewBearerToken));

            // Single-use: the same PIN again is refused.
            var reuse = await auth.Handshake.HandleAsync(MakeHandshake(pin: validPin), ctx, default);
            Assert.Equal(HandshakeStatus.PinInvalid, reuse.Status);
        }
        finally
        {
            if (File.Exists(tokenPath)) File.Delete(tokenPath);
        }
    }

    /// <summary>
    /// FR-013b: a loopback bridge keeps the parameterless auto-accept handler — a no-PIN handshake
    /// returns <see cref="HandshakeStatus.Ok"/> and no pairing service is constructed.
    /// </summary>
    [Fact]
    public async Task BuildBridgeAuth_loopback_auto_accepts_no_pin()
    {
        var bridge = new BridgeOptions { Enabled = true, BindAddress = "127.0.0.1", Port = 47291 };

        var auth = EngineHost.BuildBridgeAuth(bridge);

        Assert.Null(auth.Pairing);                  // no pairing service in loopback mode
        var ok = await auth.Handshake.HandleAsync(MakeHandshake(pin: null), MinimalContext(), default);
        Assert.Equal(HandshakeStatus.Ok, ok.Status);
    }

    /// <summary>
    /// FR-013b: a disabled or absent bridge composes no pairing service (parameterless handler),
    /// so the named-pipe / IDE-plugin path is untouched.
    /// </summary>
    [Fact]
    public void BuildBridgeAuth_disabled_or_absent_has_no_pairing_service()
    {
        Assert.Null(EngineHost.BuildBridgeAuth(null).Pairing);
        Assert.Null(EngineHost.BuildBridgeAuth(new BridgeOptions { Enabled = false }).Pairing);
    }

    /// <summary>
    /// Spec 026 (M4 closure) L8 / lan-auth-composition-contract C4(4): the LAN composition must
    /// register the SAME full non-handshake handler set on one router as the loopback composition
    /// (no regression to the spec-025 dual-transport composition). Previously only the loopback
    /// composition's handler set was asserted.
    /// </summary>
    [Fact]
    public void Lan_and_loopback_compositions_register_the_same_router_handlers()
    {
        var tokenPath = Path.Combine(Path.GetTempPath(), $"akml-tokens-{Guid.NewGuid():N}.json");
        try
        {
            var loopback = EngineComposition.Build();   // no LAN handshake supplied
            var lanAuth = EngineHost.BuildBridgeAuth(new BridgeOptions
            {
                Enabled = true,
                BindAddress = "0.0.0.0",
                Port = 47291,
                TokenStorePath = tokenPath,
            });
            var lan = EngineComposition.Build(lanAuth.Handshake);

            // Both register the handshake handler...
            Assert.True(lan.Router.IsRegistered(MessageTypes.HandshakeRequest));
            Assert.True(loopback.Router.IsRegistered(MessageTypes.HandshakeRequest));

            // ...and the identical full set of message types (handshake + every non-handshake handler).
            Assert.Equal(
                loopback.Router.RegisteredMessageTypes.OrderBy(t => t),
                lan.Router.RegisteredMessageTypes.OrderBy(t => t));
        }
        finally
        {
            if (File.Exists(tokenPath)) File.Delete(tokenPath);
        }
    }

    private static HandshakeRequest MakeHandshake(string? pin) => new()
    {
        PairingPin = pin,
        BrowserLabel = "test",
        ProtocolVersionMin = 1,
        ProtocolVersionMax = 1000,
    };

    private static RpcContext MinimalContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        SettingsLoader = () => new AppSettings(),
    };
}
