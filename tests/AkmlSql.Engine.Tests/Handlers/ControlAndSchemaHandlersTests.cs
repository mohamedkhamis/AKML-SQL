using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Handlers.Control;
using AkmlSql.Engine.Handlers.Schema;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using MessagePack;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 021 (web edition) -- M0.3 tasks T017 + T018. Smoke tests for the schema and
/// control handlers. Functional behaviour (SchemaCacheManager / SessionManager) stays
/// covered by their existing dedicated tests.
/// </summary>
public sealed class ControlAndSchemaHandlersTests
{
    private static RpcContext CreateContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        SettingsLoader = () => new AppSettings(),
    };

    [Fact]
    public async Task PingHandler_returns_engine_status_with_zero_sessions_when_idle()
    {
        var handler = new PingHandler();
        var ctx = CreateContext();
        var response = await handler.HandleAsync(new EngineStatusInfo(), ctx, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(0, response.ActiveSessions);
        Assert.Equal(0, response.CachedDatabases);
        Assert.True(response.MemoryUsageMb >= 0);
    }

    [Fact]
    public void PingHandler_tolerates_empty_payload_via_adapter()
    {
        var handler = new PingHandler();
        var iface = (IRpcRequestHandler<EngineStatusInfo, EngineStatusInfo>)handler;
        Assert.True(iface.AllowsEmptyPayload);
        Assert.Equal(MessageTypes.Ping, iface.RequestMessageType);
        Assert.Equal(MessageTypes.Pong, iface.ResponseMessageType);
    }

    [Fact]
    public async Task ShutdownHandler_throws_OperationCanceledException()
    {
        var handler = new ShutdownHandler();
        var ctx = CreateContext();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.HandleAsync(new EngineStatusInfo(), ctx, CancellationToken.None));
    }

    [Fact]
    public async Task ShutdownHandler_via_adapter_propagates_OCE_to_tear_down_pipe_loop()
    {
        // Default SwallowCancellation=false means the adapter re-throws OCE, which matches
        // the previous inline behaviour that caused PipeRpcServer's outer catch + RunAsync
        // to exit.
        var handler = new ShutdownHandler();
        var iface = (IRpcRequestHandler<EngineStatusInfo, EngineStatusInfo>)handler;
        Assert.False(iface.SwallowCancellation);

        var adapter = new TypedHandlerAdapter<EngineStatusInfo, EngineStatusInfo>(handler, CreateContext());
        var msg = new RpcMessage
        {
            MessageType = MessageTypes.Shutdown,
            RequestId = 0,
            Payload = MessagePackSerializer.Serialize(new EngineStatusInfo()),
        };
        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.HandleAsync(msg, CancellationToken.None));
    }

    [Fact]
    public async Task DocumentChangedHandler_updates_session_document()
    {
        var handler = new DocumentChangedHandler();
        var ctx = CreateContext();

        // Seed a session via UpdateSession (UpdateDocument no-ops on unknown sessions).
        ctx.Sessions.UpdateSession(new ConnectionInfo
        {
            SessionId = "S1",
            ConnectionString = string.Empty,
            DatabaseName = "tempdb",
            ServerVersion = 16,
        });

        var change = new DocumentChange
        {
            SessionId = "S1",
            ChangeType = 0,         // 0 = Full
            FullText = "SELECT 1;",
        };
        _ = await handler.HandleAsync(change, ctx, CancellationToken.None);

        var session = ctx.Sessions.GetSession("S1");
        Assert.NotNull(session);
        Assert.Equal("SELECT 1;", session!.DocumentText);
    }

    [Fact]
    public void SchemaRefreshHandler_invokes_callback_with_request()
    {
        RefreshRequest? captured = null;
        var handler = new SchemaRefreshHandler(req => { captured = req; });

        var iface = (IRpcRequestHandler<RefreshRequest, RefreshRequest>)handler;
        Assert.Equal(0, iface.ResponseMessageType);   // notification

        var request = new RefreshRequest { SessionId = "abc" };
        _ = handler.HandleAsync(request, CreateContext(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("abc", captured!.SessionId);
    }

    [Fact]
    public async Task SchemaStatusHandler_returns_empty_response_for_unknown_session()
    {
        var handler = new SchemaStatusHandler();
        var ctx = CreateContext();

        var response = await handler.HandleAsync(
            new SchemaStatusRequest { SessionId = "no-such-session" },
            ctx,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.False(response.Exists);
        Assert.Equal(0, response.ObjectCount);
    }
}
