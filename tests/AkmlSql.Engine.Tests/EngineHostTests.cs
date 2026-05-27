using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Engine.Transports;
using MessagePack;
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
}
