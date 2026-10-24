using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Engine.Transports;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.Transports;

/// <summary>
/// Spec 043 (T021, research R3, contracts/ipc.md §2) — the web transport answers a browser's requests concurrently. The
/// browser keeps one socket for every query tab, so with the old read → handle → write loop a long query in one tab held
/// up every other tab's completion, execute and cancel. Notifications (request id 0) are still applied in arrival
/// order before anything read after them; replies may come back out of order and are written whole, one at a time.
/// </summary>
public sealed class WebSocketTransportConcurrencyTests
{
    private const int SlowType = 9101;
    private const int FastType = 9102;
    private const int NotifyType = 9103;
    private const int ReadFlagType = 9104;
    private const int ThrowType = 9105;

    private static int NextPort() => 52291 + Random.Shared.Next(1000, 9000);

    private static async Task<(WebSocketTransport Transport, int Port)> StartAsync(Func<RpcMessage, CancellationToken, Task<RpcMessage?>> handler)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var port = NextPort();
            var transport = new WebSocketTransport(new WebSocketTransportOptions { BindAddress = "127.0.0.1", Port = port });
            transport.RequestReceived += handler;
            try
            {
                await transport.StartAsync(CancellationToken.None);
                return (transport, port);
            }
            catch (InvalidOperationException)
            {
                await transport.DisposeAsync();
            }
        }
        throw new InvalidOperationException("Could not bind a free localhost port for the test.");
    }

    private static async Task<ClientWebSocket> ConnectAsync(int port)
    {
        var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        return client;
    }

    private static Task SendAsync(ClientWebSocket client, RpcMessage message) =>
        client.SendAsync(new ArraySegment<byte>(MessagePackSerializer.Serialize(message)), WebSocketMessageType.Binary, true, CancellationToken.None);

    private static async Task<RpcMessage> ReceiveAsync(ClientWebSocket client, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var ms = new System.IO.MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("closed");
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);
        return MessagePackSerializer.Deserialize<RpcMessage>(ms.ToArray());
    }

    private static RpcMessage Reply(RpcMessage request, object? payload = null) => new()
    {
        MessageType = request.MessageType + 1000,
        RequestId = request.RequestId,
        Payload = payload == null ? request.Payload : MessagePackSerializer.Serialize(payload),
    };

    [Fact]
    public async Task A_slow_request_does_not_hold_up_a_later_one()
    {
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (transport, port) = await StartAsync(async (msg, ct) =>
        {
            if (msg.MessageType == SlowType)
            {
                slowStarted.TrySetResult();
                await releaseSlow.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }
            return Reply(msg);
        });
        try
        {
            using var client = await ConnectAsync(port);

            // Tab A runs a long query; tab B asks for completion while it runs.
            await SendAsync(client, new RpcMessage { MessageType = SlowType, RequestId = 1 });
            await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await SendAsync(client, new RpcMessage { MessageType = FastType, RequestId = 2 });

            var first = await ReceiveAsync(client, TimeSpan.FromSeconds(5));
            Assert.Equal(2, first.RequestId);   // answered while request 1 is still running

            releaseSlow.TrySetResult();
            var second = await ReceiveAsync(client, TimeSpan.FromSeconds(5));
            Assert.Equal(1, second.RequestId);
        }
        finally
        {
            releaseSlow.TrySetResult();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_notification_is_applied_before_a_request_read_after_it()
    {
        // DocumentChanged then Completion: the completion must see the new text.
        int applied = 0;
        var (transport, port) = await StartAsync(async (msg, ct) =>
        {
            if (msg.MessageType == NotifyType)
            {
                await Task.Delay(300, ct);   // a slow notification handler
                Interlocked.Exchange(ref applied, 1);
                return null;
            }
            if (msg.MessageType == ReadFlagType) return Reply(msg, Volatile.Read(ref applied));
            return Reply(msg);
        });
        try
        {
            using var client = await ConnectAsync(port);
            await SendAsync(client, new RpcMessage { MessageType = NotifyType, RequestId = 0 });
            await SendAsync(client, new RpcMessage { MessageType = ReadFlagType, RequestId = 7 });

            var reply = await ReceiveAsync(client, TimeSpan.FromSeconds(5));
            Assert.Equal(7, reply.RequestId);
            Assert.Equal(1, MessagePackSerializer.Deserialize<int>(reply.Payload!));
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task Many_overlapping_replies_arrive_whole_and_each_with_its_own_payload()
    {
        var (transport, port) = await StartAsync(async (msg, ct) =>
        {
            await Task.Delay(Random.Shared.Next(0, 40), ct);
            return Reply(msg, new string('x', 50_000) + msg.RequestId);   // big enough to need several socket writes
        });
        try
        {
            using var client = await ConnectAsync(port);
            const int count = 40;
            for (int i = 1; i <= count; i++) await SendAsync(client, new RpcMessage { MessageType = FastType, RequestId = i });

            var seen = new ConcurrentDictionary<int, bool>();
            for (int i = 0; i < count; i++)
            {
                var reply = await ReceiveAsync(client, TimeSpan.FromSeconds(10));
                var text = MessagePackSerializer.Deserialize<string>(reply.Payload!);
                Assert.EndsWith(reply.RequestId.ToString(), text);
                Assert.True(seen.TryAdd(reply.RequestId, true), $"request {reply.RequestId} answered twice");
            }
            Assert.Equal(Enumerable.Range(1, count), seen.Keys.OrderBy(k => k));
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_failing_handler_still_answers_its_own_request_with_an_error()
    {
        var (transport, port) = await StartAsync((msg, ct) =>
            msg.MessageType == ThrowType ? throw new InvalidOperationException("boom") : Task.FromResult<RpcMessage?>(Reply(msg)));
        try
        {
            using var client = await ConnectAsync(port);
            await SendAsync(client, new RpcMessage { MessageType = ThrowType, RequestId = 3 });
            await SendAsync(client, new RpcMessage { MessageType = FastType, RequestId = 4 });

            var replies = new[] { await ReceiveAsync(client, TimeSpan.FromSeconds(5)), await ReceiveAsync(client, TimeSpan.FromSeconds(5)) };
            var error = Assert.Single(replies, r => r.RequestId == 3);
            Assert.Equal(MessageTypes.Error, error.MessageType);
            Assert.Single(replies, r => r.RequestId == 4);
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }
}
