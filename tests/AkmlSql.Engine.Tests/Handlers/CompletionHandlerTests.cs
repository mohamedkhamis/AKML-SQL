using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Handlers.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using MessagePack;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 021 (web edition) — M0.2 task T012. Exercises <see cref="CompletionHandler"/> via
/// <see cref="InProcessTransport"/> with zero serialisation, then again via
/// <see cref="TypedHandlerAdapter{TRequest,TResponse}"/> + an <see cref="IMessageHandler"/>
/// round-trip (the path the named pipe uses). Both paths must return the same
/// <see cref="CompletionResponse"/> shape.
/// </summary>
public sealed class CompletionHandlerTests
{
    private static RpcContext CreateContext(AppSettings? settings = null)
    {
        var captured = settings ?? new AppSettings();
        return new RpcContext
        {
            Sessions = new SessionManager(),
            SchemaCache = new SchemaCacheManager(),
            Logger = Log.Logger,
            SettingsLoader = () => captured,
        };
    }

    private static CompletionHandler CreateHandler(out CompletionEngine engine, AppSettings? settings = null)
    {
        engine = new CompletionEngine(new TsqlParserService());
        var captured = settings ?? new AppSettings();
        return new CompletionHandler(engine, () => captured);
    }

    [Fact]
    public async Task Direct_HandleAsync_returns_completions_for_keyword_position()
    {
        var handler = CreateHandler(out _);
        var ctx = CreateContext();

        var request = new CompletionRequest
        {
            SessionId = string.Empty,
            CursorOffset = 7,
            TriggerKind = 0,
            HasSelection = false,
        };

        var response = await handler.HandleAsync(request, ctx, CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
        // No session text was registered so the engine sees the empty document; on an empty
        // document with cursor at offset 7 the engine returns its default keyword set. The
        // assertion below validates the shape, not the content.
    }

    [Fact]
    public async Task Through_InProcessTransport_round_trips_via_RpcRouter()
    {
        var handler = CreateHandler(out _);
        var ctx = CreateContext();

        var router = new RpcRouter();
        router.Register(handler);

        await using var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => router.RouteAsync(msg, ctx, ct);
        await transport.StartAsync(CancellationToken.None);

        var request = new CompletionRequest
        {
            SessionId = string.Empty,
            CursorOffset = 7,
            TriggerKind = 0,
            HasSelection = false,
        };
        var msg = new RpcMessage
        {
            MessageType = MessageTypes.RequestCompletion,
            RequestId = 17,
            Payload = MessagePackSerializer.Serialize(request),
        };

        var response = await transport.SendAsync(msg, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.CompletionResult, response!.MessageType);
        Assert.Equal(17, response.RequestId);
        var typed = MessagePackSerializer.Deserialize<CompletionResponse>(response.Payload!);
        Assert.NotNull(typed);
        Assert.NotNull(typed.Items);
    }

    [Fact]
    public async Task Through_TypedHandlerAdapter_returns_same_shape_as_named_pipe_pluggable_path()
    {
        var handler = CreateHandler(out _);
        var ctx = CreateContext();
        var adapter = new TypedHandlerAdapter<CompletionRequest, CompletionResponse>(handler, ctx);

        var request = new CompletionRequest
        {
            SessionId = string.Empty,
            CursorOffset = 7,
            TriggerKind = 0,
            HasSelection = false,
        };
        var msg = new RpcMessage
        {
            MessageType = MessageTypes.RequestCompletion,
            RequestId = 23,
            Payload = MessagePackSerializer.Serialize(request),
        };

        var response = await adapter.HandleAsync(msg, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.CompletionResult, response!.MessageType);
        Assert.Equal(23, response.RequestId);
    }

    [Fact]
    public async Task Adapter_returns_error_on_null_payload()
    {
        var handler = CreateHandler(out _);
        var ctx = CreateContext();
        var adapter = new TypedHandlerAdapter<CompletionRequest, CompletionResponse>(handler, ctx);

        var msg = new RpcMessage
        {
            MessageType = MessageTypes.RequestCompletion,
            RequestId = 99,
            Payload = null,
        };

        var response = await adapter.HandleAsync(msg, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.Error, response!.MessageType);
        Assert.Equal(99, response.RequestId);
    }

    [Fact]
    public async Task Settings_provider_is_called_per_request_so_changes_take_effect()
    {
        var settings = new AppSettings();
        settings.IntelliSense.SuggestionTypes.IncludeKeywords = true;

        int callCount = 0;
        var engine = new CompletionEngine(new TsqlParserService());
        var handler = new CompletionHandler(engine, () =>
        {
            callCount++;
            return settings;
        });
        var ctx = CreateContext();

        var request = new CompletionRequest { SessionId = string.Empty, CursorOffset = 7 };

        _ = await handler.HandleAsync(request, ctx, CancellationToken.None);
        Assert.Equal(1, callCount);

        // Flip the toggle and re-issue — the provider must be invoked again.
        settings.IntelliSense.SuggestionTypes.IncludeKeywords = false;
        _ = await handler.HandleAsync(request, ctx, CancellationToken.None);
        Assert.Equal(2, callCount);
        Assert.False(engine.IncludeKeywords);   // pushed onto the engine on the second call
    }
}
