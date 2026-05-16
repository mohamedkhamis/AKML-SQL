using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;
using Serilog;

namespace AkmlSql.Engine.Handlers.Control
{
    // Spec 021 (web edition) -- M0.3 task T018. Control / lifecycle handlers:
    //   * DocumentChangedHandler -- notification; updates the session's document text.
    //   * PingHandler            -- synchronous; returns engine status (cached DBs, sessions).
    //   * ShutdownHandler        -- notification that intentionally throws
    //                               OperationCanceledException to tear down the pipe loop.
    //
    // ConnectionChanged is deliberately NOT migrated in this batch -- it carries multiple
    // side effects (parser version, completion provider cache invalidation, fire-and-forget
    // Phase A/B population) that touch more engine internals than a thin handler should.
    // Migration of ConnectionChanged needs the SchemaMetadataService and DatabaseProvider
    // statics to live in RpcContext first.

    public sealed class DocumentChangedHandler : IRpcRequestHandler<DocumentChange, DocumentChange>
    {
        public int RequestMessageType => MessageTypes.DocumentChanged;
        public int ResponseMessageType => 0;    // notification, no reply

        public Task<DocumentChange> HandleAsync(DocumentChange request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            ctx.Sessions.UpdateDocument(request);
            return Task.FromResult(request);
        }
    }

    public sealed class PingHandler : IRpcRequestHandler<EngineStatusInfo, EngineStatusInfo>
    {
        public int RequestMessageType => MessageTypes.Ping;
        public int ResponseMessageType => MessageTypes.Pong;
        public bool AllowsEmptyPayload => true;

        public Task<EngineStatusInfo> HandleAsync(EngineStatusInfo request, RpcContext ctx, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var status = new EngineStatusInfo
            {
                MemoryUsageMb = (int)(GC.GetTotalMemory(false) / (1024 * 1024)),
                CachedDatabases = ctx.SchemaCache.CacheCount,
                ActiveSessions = ctx.Sessions.SessionCount,
                UptimeSeconds = 0,
            };
            return Task.FromResult(status);
        }
    }

    public sealed class ShutdownHandler : IRpcRequestHandler<EngineStatusInfo, EngineStatusInfo>
    {
        public int RequestMessageType => MessageTypes.Shutdown;
        public int ResponseMessageType => 0;    // notification; the OCE tears the loop down before any response
        public bool AllowsEmptyPayload => true;

        public Task<EngineStatusInfo> HandleAsync(EngineStatusInfo request, RpcContext ctx, CancellationToken ct)
        {
            Log.Information("Shutdown requested by client.");
            throw new OperationCanceledException("Shutdown requested");
        }
    }
}
