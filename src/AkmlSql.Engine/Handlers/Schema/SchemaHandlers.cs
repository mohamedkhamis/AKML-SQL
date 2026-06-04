using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Schema
{
    // Spec 021 (web edition) -- M0.3 task T017. Two typed handlers for schema operations:
    //   * SchemaRefreshHandler  -- notification (no response). Invokes a host-supplied
    //                              callback that drives NamedPipeTransport.HandleSchemaRefreshRequest.
    //                              The callback model keeps the substantial helper method in
    //                              NamedPipeTransport for now; a future task may move the helper
    //                              onto a dedicated SchemaRefreshService.
    //   * SchemaStatusHandler   -- synchronous query against the schema cache.

    public sealed class SchemaRefreshHandler : IRpcRequestHandler<RefreshRequest, RefreshRequest>
    {
        private readonly Action<RefreshRequest> _refresh;

        public SchemaRefreshHandler(Action<RefreshRequest> refresh)
        {
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        }

        public int RequestMessageType => MessageTypes.SchemaRefreshRequest;
        public int ResponseMessageType => 0;    // notification, no reply

        public Task<RefreshRequest> HandleAsync(RefreshRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            _refresh(request);
            return Task.FromResult(request);    // placeholder; ResponseMessageType=0 means it is dropped
        }
    }

    public sealed class SchemaStatusHandler : IRpcRequestHandler<SchemaStatusRequest, SchemaStatusResponse>
    {
        public int RequestMessageType => MessageTypes.SchemaStatusRequest;
        public int ResponseMessageType => MessageTypes.SchemaStatusResponse;

        public Task<SchemaStatusResponse> HandleAsync(SchemaStatusRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var session = !string.IsNullOrEmpty(request.SessionId)
                ? ctx.Sessions.GetSession(request.SessionId)
                : null;

            var response = new SchemaStatusResponse();
            if (session == null || string.IsNullOrEmpty(session.DatabaseName))
            {
                return Task.FromResult(response);
            }

            response.DatabaseName = session.DatabaseName;
            var cache = ctx.SchemaCache.GetCache(request.SessionId, session.DatabaseName);
            if (cache == null)
            {
                return Task.FromResult(response);
            }

            response.Exists = true;
            response.Phase = (int)cache.Phase;
            response.AuthError = cache.PermissionDenied;

            int objectCount = 0;
            int columnsLoaded = 0;
            foreach (var schema in cache.Schemas.Values)
            {
                foreach (var obj in schema.Objects)
                {
                    objectCount++;
                    if (obj.ColumnsLoaded) columnsLoaded++;
                }
            }
            response.ObjectCount = objectCount;
            response.ColumnsLoadedCount = columnsLoaded;

            return Task.FromResult(response);
        }
    }
}
