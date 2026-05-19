using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Handlers.Analysis;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using MessagePack;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 021 (web edition) -- M0.3 task T014. Exercises <see cref="AnalysisHandler"/> and
/// <see cref="AnalysisSettingsChangedHandler"/> via <see cref="TypedHandlerAdapter{TRequest,TResponse}"/>.
/// </summary>
public sealed class AnalysisHandlersTests
{
    private static RpcContext CreateContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        SettingsLoader = () => new AppSettings(),
    };

    private static AnalysisEngine CreateAnalysisEngine() =>
        new(new TsqlParserService(), new RuleRegistry(), new CaSettingsLoader());

    [Fact]
    public async Task AnalysisHandler_returns_response_via_RpcRouter()
    {
        var engine = CreateAnalysisEngine();
        var handler = new AnalysisHandler(engine, () => new AppSettings());

        var router = new RpcRouter();
        router.Register(handler);
        var ctx = CreateContext();

        await using var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => router.RouteAsync(msg, ctx, ct);
        await transport.StartAsync(CancellationToken.None);

        var request = new CodeAnalysisRequest
        {
            SessionId = "test-session",
            RequestId = Guid.NewGuid().ToString(),
            DocumentText = "SELECT * FROM dbo.Foo;",
            DocumentVersion = 1,
        };
        var msg = new RpcMessage
        {
            MessageType = MessageTypes.RequestAnalyze,
            RequestId = 1,
            Payload = MessagePackSerializer.Serialize(request),
        };

        var response = await transport.SendAsync(msg, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.AnalysisResult, response!.MessageType);
        var typed = MessagePackSerializer.Deserialize<CodeAnalysisResponse>(response.Payload!);
        Assert.NotNull(typed);
        // SELECT * is a classic PE001 finding; assert the issues array is set to verify wiring.
        Assert.NotNull(typed.Issues);
    }

    [Fact]
    public async Task AnalysisHandler_swallows_OperationCanceledException_into_null_response()
    {
        var engine = CreateAnalysisEngine();
        // Cancellation token is pre-cancelled; engine respects the token and throws OCE.
        var handler = new AnalysisHandler(engine, () => new AppSettings());
        Assert.True(((IRpcRequestHandler<CodeAnalysisRequest, CodeAnalysisResponse>)handler).SwallowCancellation);

        var ctx = CreateContext();
        var adapter = new TypedHandlerAdapter<CodeAnalysisRequest, CodeAnalysisResponse>(handler, ctx);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // The adapter's OCE-catch is keyed on `SwallowCancellation`. We pass a pre-cancelled
        // token via the inbound RpcMessage's adapter-level CancellationToken; the engine
        // checks the token early and throws OCE. The adapter must convert that to a null
        // response (no Error frame written).
        var msg = new RpcMessage
        {
            MessageType = MessageTypes.RequestAnalyze,
            RequestId = 7,
            Payload = MessagePackSerializer.Serialize(new CodeAnalysisRequest
            {
                SessionId = string.Empty,
                RequestId = string.Empty,
                DocumentText = "SELECT 1;",
                DocumentVersion = 1,
            }),
        };

        var response = await adapter.HandleAsync(msg, cts.Token);

        Assert.Null(response);
    }

    [Fact]
    public async Task AnalysisSettingsChangedHandler_invokes_callback_and_returns_no_response()
    {
        int invoked = 0;
        var handler = new AnalysisSettingsChangedHandler(() => { invoked++; });

        var iface = (IRpcRequestHandler<AnalysisSettingsChangedRequest, AnalysisSettingsChangedRequest>)handler;
        Assert.Equal(0, iface.ResponseMessageType);   // notification
        Assert.True(iface.AllowsEmptyPayload);

        var ctx = CreateContext();
        var adapter = new TypedHandlerAdapter<AnalysisSettingsChangedRequest, AnalysisSettingsChangedRequest>(handler, ctx);

        // Shell sends "new { }" which serialises to a 1-byte empty map (0x80) -- not null.
        var msg = new RpcMessage
        {
            MessageType = MessageTypes.AnalysisSettingsChanged,
            RequestId = 0,
            Payload = MessagePackSerializer.Serialize(new AnalysisSettingsChangedRequest()),
        };

        var response = await adapter.HandleAsync(msg, CancellationToken.None);

        Assert.Null(response);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task AnalysisSettingsChangedHandler_tolerates_null_payload()
    {
        int invoked = 0;
        var handler = new AnalysisSettingsChangedHandler(() => { invoked++; });
        var ctx = CreateContext();
        var adapter = new TypedHandlerAdapter<AnalysisSettingsChangedRequest, AnalysisSettingsChangedRequest>(handler, ctx);

        var msg = new RpcMessage
        {
            MessageType = MessageTypes.AnalysisSettingsChanged,
            RequestId = 0,
            Payload = null,
        };

        var response = await adapter.HandleAsync(msg, CancellationToken.None);

        Assert.Null(response);
        Assert.Equal(1, invoked);
    }
}
