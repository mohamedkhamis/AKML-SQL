using System.IO.Pipes;
using AkmlSql.Core.Ipc;
using AkmlSql.Engine.Transports;
using Xunit;

namespace AkmlSql.Engine.Tests;

/// <summary>
/// The named-pipe dispatch loop must not serialize long-running AI requests with everything
/// else. It used to: read → await the handler to completion → write → read next, so ONE slow
/// provider call (e.g. a ghost-text Gemini request that ran to the 90 s provider timeout after
/// the shell had long given up — the dispatch token is the pipe-lifetime token, nothing cancels
/// the abandoned handler) froze completions, schema-status polls ("Refreshing schema cache…"
/// forever) and every queued AI request ("Error: A task was canceled") behind it. AI message
/// types (70–78) now dispatch on the thread pool; fast handlers stay strictly serial.
/// </summary>
public sealed class NamedPipeTransportConcurrencyTests
{
    [Fact]
    public async Task A_blocked_ai_request_does_not_stall_fast_requests()
    {
        var pipeName = "akml-test-" + Guid.NewGuid().ToString("N");
        var aiStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var transport = new NamedPipeTransport(pipeName);
        transport.RequestReceived += async (msg, ct) =>
        {
            if (msg.MessageType == MessageTypes.AiChat)
            {
                aiStarted.TrySetResult();
                await releaseAi.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }
            return new RpcMessage { MessageType = msg.MessageType + 100, RequestId = msg.RequestId };
        };
        await transport.StartAsync(CancellationToken.None);

        await using var client = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);

        // 1. An AI request whose handler blocks (a slow provider call).
        await FrameProtocol.WriteFramedAsync(client,
            new RpcMessage { MessageType = MessageTypes.AiChat, RequestId = 1 });
        await aiStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 2. A fast request sent while the AI handler is still blocked MUST be answered first.
        await FrameProtocol.WriteFramedAsync(client,
            new RpcMessage { MessageType = MessageTypes.SchemaStatusRequest, RequestId = 2 });
        var first = await FrameProtocol.ReadFramedAsync(client).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(first);
        Assert.Equal(2, first!.RequestId);

        // 3. Releasing the AI handler delivers its (late, out-of-order) response too.
        releaseAi.TrySetResult();
        var second = await FrameProtocol.ReadFramedAsync(client).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(second);
        Assert.Equal(1, second!.RequestId);
    }
}
