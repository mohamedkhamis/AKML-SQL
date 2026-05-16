using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.Transports;

/// <summary>
/// Spec 021 (web edition) -- M3 task T059. End-to-end coverage of the engine-side
/// <see cref="WebSocketTransport"/> using a real <see cref="ClientWebSocket"/> client.
/// LAN-mode WSS round-trips (T058) are deferred -- they need an installer-generated
/// self-signed cert plus the Kestrel-hosted variant.
/// </summary>
public sealed class WebSocketTransportTests
{
    private static int NextPort() => 47291 + Random.Shared.Next(1000, 9000);

    private static async Task<(WebSocketTransport, int)> StartLocalhostAsync(
        Func<RpcMessage, CancellationToken, Task<RpcMessage?>> handler)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var port = NextPort();
            var transport = new WebSocketTransport(new WebSocketTransportOptions
            {
                BindAddress = "127.0.0.1",
                Port = port,
            });
            transport.RequestReceived += handler;
            try
            {
                await transport.StartAsync(CancellationToken.None);
                return (transport, port);
            }
            catch (InvalidOperationException)
            {
                await transport.DisposeAsync();
                // Port collision -- retry with a different port.
            }
        }
        throw new InvalidOperationException("Could not bind a free localhost port for the test.");
    }

    private static async Task<RpcMessage?> RoundTripAsync(int port, RpcMessage request, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);

        var payload = MessagePackSerializer.Serialize(request, cancellationToken: cts.Token);
        await client.SendAsync(
            new ArraySegment<byte>(payload), WebSocketMessageType.Binary, true, cts.Token);

        using var ms = new System.IO.MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return MessagePackSerializer.Deserialize<RpcMessage>(ms.ToArray(), cancellationToken: cts.Token);
    }

    [Fact]
    public async Task Round_trip_echo_handler_returns_response()
    {
        var (transport, port) = await StartLocalhostAsync((msg, ct) =>
        {
            // Echo handler: bump RequestId in the response so we can verify the
            // request/response pairing.
            var response = new RpcMessage
            {
                MessageType = msg.MessageType + 1000,
                RequestId = msg.RequestId,
                Payload = msg.Payload,
            };
            return Task.FromResult<RpcMessage?>(response);
        });

        try
        {
            var request = new RpcMessage
            {
                MessageType = 9001,
                RequestId = 42,
                Payload = MessagePackSerializer.Serialize("hello"),
            };
            var response = await RoundTripAsync(port, request);

            Assert.NotNull(response);
            Assert.Equal(10001, response!.MessageType);
            Assert.Equal(42, response.RequestId);
            Assert.Equal("hello", MessagePackSerializer.Deserialize<string>(response.Payload!));
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task Handler_returning_null_keeps_socket_open_with_no_response()
    {
        // Notifications (handler returns null) produce no frame on the wire. The client
        // should not receive anything; we time out the receive intentionally.
        var (transport, port) = await StartLocalhostAsync((_, _) => Task.FromResult<RpcMessage?>(null));

        try
        {
            var request = new RpcMessage
            {
                MessageType = 9002,
                RequestId = 0,
                Payload = Array.Empty<byte>(),
            };

            // Use a short timeout because we expect no response. ThrowsAnyAsync rather
            // than ThrowsAsync because the actual type is TaskCanceledException (a
            // subtype of OperationCanceledException) and Throws expects an EXACT match.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => RoundTripAsync(port, request, TimeSpan.FromSeconds(1)));
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task Handler_exception_yields_Error_response()
    {
        var (transport, port) = await StartLocalhostAsync((_, _) =>
            throw new InvalidOperationException("simulated handler failure"));

        try
        {
            var request = new RpcMessage
            {
                MessageType = 9003,
                RequestId = 7,
                Payload = Array.Empty<byte>(),
            };
            var response = await RoundTripAsync(port, request);

            Assert.NotNull(response);
            Assert.Equal(MessageTypes.Error, response!.MessageType);
            Assert.Equal(7, response.RequestId);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public void Construction_rejects_lan_binding_without_tls()
    {
        // Spec 021 FR-013a: LAN mode requires TLS. Constructor refuses plaintext LAN bind.
        Assert.Throws<InvalidOperationException>(() =>
            new WebSocketTransport(new WebSocketTransportOptions
            {
                BindAddress = "0.0.0.0",
                Port = 12345,
                TlsCertPath = null,
            }));
    }

    [Fact]
    public async Task Cannot_start_twice()
    {
        var (transport, _) = await StartLocalhostAsync((_, _) => Task.FromResult<RpcMessage?>(null));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => transport.StartAsync(CancellationToken.None));
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }
}
