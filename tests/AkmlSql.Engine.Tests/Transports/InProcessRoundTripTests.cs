using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Engine;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using MessagePack;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Transports;

/// <summary>
/// Spec 021 (web edition) — M0 transport abstraction validation.
/// Exercises the new <see cref="IRpcTransport"/> / <see cref="RpcRouter"/> /
/// <see cref="IRpcRequestHandler{TRequest,TResponse}"/> wiring end-to-end with a sample
/// handler. This is the foundation that T012 onwards re-validates with the migrated
/// production handlers (CompletionHandler, FormatHandler, AnalysisHandler, etc.).
/// </summary>
public sealed class InProcessRoundTripTests
{
    [MessagePackObject]
    public sealed class EchoRequest
    {
        [Key(0)] public string Payload { get; set; } = string.Empty;
    }

    [MessagePackObject]
    public sealed class EchoResponse
    {
        [Key(0)] public string Echoed { get; set; } = string.Empty;
        [Key(1)] public int Length { get; set; }
    }

    /// <summary>Echo handler — uppercases the payload and reports its length.</summary>
    private sealed class EchoHandler : IRpcRequestHandler<EchoRequest, EchoResponse>
    {
        public int RequestMessageType => 9001;   // outside the production range
        public int ResponseMessageType => 9101;

        public Task<EchoResponse> HandleAsync(EchoRequest request, RpcContext ctx, CancellationToken ct)
        {
            ctx.Logger.Verbose("echo received");
            return Task.FromResult(new EchoResponse
            {
                Echoed = request.Payload.ToUpperInvariant(),
                Length = request.Payload.Length,
            });
        }
    }

    /// <summary>Fire-and-forget handler — ResponseMessageType=0 means "no reply frame".</summary>
    private sealed class NotificationHandler : IRpcRequestHandler<EchoRequest, EchoResponse>
    {
        public int RequestMessageType => 9002;
        public int ResponseMessageType => 0;
        public int Calls { get; private set; }

        public Task<EchoResponse> HandleAsync(EchoRequest request, RpcContext ctx, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new EchoResponse());
        }
    }

    private static RpcContext CreateContext()
    {
        return new RpcContext
        {
            Sessions = new SessionManager(),
            SchemaCache = new SchemaCacheManager(),
            Logger = Log.Logger,
            SettingsLoader = () => new AppSettings(),
        };
    }

    [Fact]
    public async Task Round_trip_through_InProcessTransport_invokes_handler_and_returns_response()
    {
        var router = new RpcRouter();
        router.Register(new EchoHandler());
        var ctx = CreateContext();

        await using var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => router.RouteAsync(msg, ctx, ct);
        await transport.StartAsync(CancellationToken.None);

        var request = new RpcMessage
        {
            MessageType = 9001,
            RequestId = 42,
            Payload = MessagePackSerializer.Serialize(new EchoRequest { Payload = "hello" }),
        };

        var response = await transport.SendAsync(request, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(9101, response!.MessageType);
        Assert.Equal(42, response.RequestId);
        var typed = MessagePackSerializer.Deserialize<EchoResponse>(response.Payload!);
        Assert.Equal("HELLO", typed.Echoed);
        Assert.Equal(5, typed.Length);
    }

    [Fact]
    public async Task Unregistered_MessageType_returns_null_response()
    {
        var router = new RpcRouter();   // no handlers
        var ctx = CreateContext();

        await using var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => router.RouteAsync(msg, ctx, ct);
        await transport.StartAsync(CancellationToken.None);

        var request = new RpcMessage
        {
            MessageType = 9999,
            RequestId = 1,
            Payload = Array.Empty<byte>(),
        };

        var response = await transport.SendAsync(request, CancellationToken.None);
        Assert.Null(response);
    }

    [Fact]
    public async Task Notification_handler_runs_but_returns_no_response()
    {
        var router = new RpcRouter();
        var handler = new NotificationHandler();
        router.Register(handler);
        var ctx = CreateContext();

        await using var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => router.RouteAsync(msg, ctx, ct);
        await transport.StartAsync(CancellationToken.None);

        var request = new RpcMessage
        {
            MessageType = 9002,
            RequestId = 0,
            Payload = MessagePackSerializer.Serialize(new EchoRequest { Payload = "fire and forget" }),
        };

        var response = await transport.SendAsync(request, CancellationToken.None);
        Assert.Null(response);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void Duplicate_registration_throws()
    {
        var router = new RpcRouter();
        router.Register(new EchoHandler());
        Assert.Throws<InvalidOperationException>(() => router.Register(new EchoHandler()));
    }

    [Fact]
    public async Task SendAsync_before_StartAsync_throws()
    {
        await using var transport = new InProcessTransport();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.SendAsync(new RpcMessage { MessageType = 1, RequestId = 1, Payload = Array.Empty<byte>() }, CancellationToken.None));
    }
}
