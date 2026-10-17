using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Completion
{
    /// <summary>
    /// Spec 021 (web edition) -- M0.4 wave 2 task T020. Wraps
    /// <see cref="WildcardExpansionHandler"/> (the engine-side handler in the Completion
    /// namespace -- not this class) as a typed <see cref="IRpcRequestHandler{TRequest,TResponse}"/>.
    /// </summary>
    public sealed class WildcardExpansionHandler : IRpcRequestHandler<WildcardExpansionRequest, WildcardExpansionResponse>
    {
        private readonly Engine.Completion.WildcardExpansionHandler _inner;
        private readonly PostgreSql.PgServices? _pg;

        public WildcardExpansionHandler(Engine.Completion.WildcardExpansionHandler inner, PostgreSql.PgServices? pg = null)
        {
            _pg = pg;
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public int RequestMessageType => MessageTypes.WildcardExpansion;
        public int ResponseMessageType => MessageTypes.WildcardExpansionResult;

        public Task<WildcardExpansionResponse> HandleAsync(WildcardExpansionRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var session = ctx.Sessions.GetSession(request.SessionId);
            var cache = session != null
                ? ctx.SchemaCache.GetCache(request.SessionId, session.DatabaseName)
                : null;
            if (session?.Kind == AkmlSql.Core.DatabaseKind.PostgreSql && _pg != null)
                return Task.FromResult(_pg.Wildcard.Handle(request.DocumentText, request.CursorOffset, request.Qualifier, cache));

            var response = _inner.Handle(request.DocumentText, request.CursorOffset, request.Qualifier, cache);
            return Task.FromResult(response);
        }
    }
}
