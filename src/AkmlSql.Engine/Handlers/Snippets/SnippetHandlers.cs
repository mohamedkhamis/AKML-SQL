using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Snippets
{
    // Spec 021 (web edition) -- M0.3 task T015. Five typed handlers wrapping the existing
    // SnippetRequestHandler. SnippetExpand needs the active session's database name from
    // RpcContext.Sessions; the rest are direct wrappers.

    public sealed class SnippetExpandHandler : IRpcRequestHandler<SnippetExpandRequest, SnippetExpandResponse>
    {
        private readonly SnippetRequestHandler _inner;
        public SnippetExpandHandler(SnippetRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.SnippetExpand;
        public int ResponseMessageType => MessageTypes.SnippetExpandResult;
        public Task<SnippetExpandResponse> HandleAsync(SnippetExpandRequest request, RpcContext ctx, CancellationToken ct)
        {
            var session = ctx.Sessions.GetSession(request.SessionId);
            return Task.FromResult(_inner.HandleExpand(request, session?.DatabaseName));
        }
    }

    public sealed class SnippetListHandler : IRpcRequestHandler<SnippetListRequest, SnippetListResponse>
    {
        private readonly SnippetRequestHandler _inner;
        public SnippetListHandler(SnippetRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.SnippetList;
        public int ResponseMessageType => MessageTypes.SnippetListResult;
        public Task<SnippetListResponse> HandleAsync(SnippetListRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleList(request));
    }

    public sealed class SnippetSaveHandler : IRpcRequestHandler<SnippetSaveRequest, SnippetSaveResponse>
    {
        private readonly SnippetRequestHandler _inner;
        public SnippetSaveHandler(SnippetRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.SnippetSave;
        public int ResponseMessageType => MessageTypes.SnippetSaveResult;
        public Task<SnippetSaveResponse> HandleAsync(SnippetSaveRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleSave(request));
    }

    public sealed class SnippetDeleteHandler : IRpcRequestHandler<SnippetDeleteRequest, SnippetDeleteResponse>
    {
        private readonly SnippetRequestHandler _inner;
        public SnippetDeleteHandler(SnippetRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.SnippetDelete;
        public int ResponseMessageType => MessageTypes.SnippetDeleteResult;
        public Task<SnippetDeleteResponse> HandleAsync(SnippetDeleteRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleDelete(request));
    }

    public sealed class SnippetImportHandler : IRpcRequestHandler<SnippetImportRequest, SnippetImportResponse>
    {
        private readonly SnippetRequestHandler _inner;
        public SnippetImportHandler(SnippetRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.SnippetImport;
        public int ResponseMessageType => MessageTypes.SnippetImportResult;
        public Task<SnippetImportResponse> HandleAsync(SnippetImportRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleImport(request));
    }
}
