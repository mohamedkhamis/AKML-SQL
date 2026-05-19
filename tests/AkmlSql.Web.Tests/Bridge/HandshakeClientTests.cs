using System;
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
/// Spec 021 (web edition) -- M3 task T071. Exercises the handshake protocol through
/// a fake WebSocket loopback: happy path, pin_invalid surfaces the right status,
/// pin_required wipes the bearer-token expectation, protocol_mismatch is reported
/// verbatim. The wire-format is the same as the production engine -- the fake just
/// stands in for the engine's transport.
/// </summary>
public sealed class HandshakeClientTests
{
    private static EngineBridge BuildBridge(Func<HandshakeRequest, HandshakeResponse> serverResponder, out FakeBridgeWebSocket socket)
    {
        FakeBridgeWebSocket capturedSocket = null!;
        capturedSocket = new FakeBridgeWebSocket(frame =>
        {
            var envelope = MessagePackSerializer.Deserialize<RpcMessage>(frame);
            if (envelope.MessageType != MessageTypes.HandshakeRequest)
            {
                // Unknown -- echo back an empty response.
                return MessagePackSerializer.Serialize(new RpcMessage
                {
                    MessageType = 0,
                    RequestId = envelope.RequestId,
                    Payload = Array.Empty<byte>(),
                });
            }
            var req = MessagePackSerializer.Deserialize<HandshakeRequest>(envelope.Payload!);
            var resp = serverResponder(req);
            return MessagePackSerializer.Serialize(new RpcMessage
            {
                MessageType = MessageTypes.HandshakeResponse,
                RequestId = envelope.RequestId,
                Payload = MessagePackSerializer.Serialize(resp),
            });
        });
        socket = capturedSocket;
        var sock = capturedSocket;
        return new EngineBridge(() => sock, new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));
    }

    private static EngineConnection Localhost() => new()
    {
        Id = "c1", Host = "127.0.0.1", Port = 5081, IsLocalhost = true,
    };

    [Fact]
    public async Task Happy_path_localhost_handshake_returns_ok_and_capabilities()
    {
        var bridge = BuildBridge(_ => new HandshakeResponse
        {
            Status = HandshakeStatus.Ok,
            EngineVersion = "1.0.0",
            ChosenProtocolVersion = 1,
            EngineCapabilities = new[] { "core.format.v1", "core.analysis.v1", "schema.v2" },
        }, out _);

        var response = await bridge.ConnectAsync(Localhost(), null, null, CancellationToken.None);

        Assert.Equal(HandshakeStatus.Ok, response.Status);
        Assert.Equal("1.0.0", response.EngineVersion);
        Assert.Equal(BridgeState.Open, bridge.State);
        Assert.Contains("core.format.v1", bridge.EngineCapabilities);
    }

    [Fact]
    public async Task Pin_invalid_surfaces_as_response_status_and_bridge_is_failed()
    {
        var bridge = BuildBridge(_ => new HandshakeResponse
        {
            Status = HandshakeStatus.PinInvalid,
            EngineVersion = "1.0.0",
            ErrorMessage = "Pairing PIN was wrong or expired.",
        }, out _);

        var response = await bridge.ConnectAsync(Localhost(), null, "bad-pin", CancellationToken.None);

        Assert.Equal(HandshakeStatus.PinInvalid, response.Status);
        Assert.Equal(BridgeState.Failed, bridge.State);
        Assert.Empty(bridge.EngineCapabilities);
    }

    [Fact]
    public async Task Pin_required_surfaces_when_engine_rejects_bearer()
    {
        var bridge = BuildBridge(_ => new HandshakeResponse
        {
            Status = HandshakeStatus.PinRequired,
            EngineVersion = "1.0.0",
            ErrorMessage = "Bearer token unknown or revoked.",
        }, out _);

        var response = await bridge.ConnectAsync(Localhost(), "stale-bearer", null, CancellationToken.None);

        Assert.Equal(HandshakeStatus.PinRequired, response.Status);
        Assert.Equal(BridgeState.Failed, bridge.State);
    }

    [Fact]
    public async Task Protocol_mismatch_is_reported_verbatim()
    {
        var bridge = BuildBridge(_ => new HandshakeResponse
        {
            Status = HandshakeStatus.ProtocolMismatch,
            EngineVersion = "2.0.0",
            ErrorMessage = "Client wants 1..1; engine supports 2..2.",
        }, out _);

        var response = await bridge.ConnectAsync(Localhost(), null, null, CancellationToken.None);

        Assert.Equal(HandshakeStatus.ProtocolMismatch, response.Status);
        Assert.Contains("Client wants 1..1", response.ErrorMessage);
    }

    [Fact]
    public async Task ConnectAsync_sends_HandshakeRequest_with_browser_label()
    {
        HandshakeRequest? observed = null;
        var bridge = BuildBridge(req =>
        {
            observed = req;
            return new HandshakeResponse { Status = HandshakeStatus.Ok, EngineVersion = "1.0.0" };
        }, out _);

        await bridge.ConnectAsync(Localhost(), null, null, CancellationToken.None);

        Assert.NotNull(observed);
        Assert.Equal("Web edition", observed!.BrowserLabel);
        Assert.Equal(1, observed.ProtocolVersionMin);
        Assert.Equal(1, observed.ProtocolVersionMax);
    }

    [Fact]
    public async Task State_transitions_through_Connecting_to_Open_on_happy_path()
    {
        var states = new System.Collections.Generic.List<BridgeState>();
        var bridge = BuildBridge(_ => new HandshakeResponse { Status = HandshakeStatus.Ok, EngineVersion = "1.0.0" }, out _);
        bridge.StateChanged += s => states.Add(s);

        await bridge.ConnectAsync(Localhost(), null, null, CancellationToken.None);

        Assert.Contains(BridgeState.Connecting, states);
        Assert.Contains(BridgeState.Open, states);
    }
}
