using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Completion
{
    /// <summary>
    /// Spec 021 (web edition) -- M0.4 wave 2 task T020. Wraps <see cref="QuickInfoProvider"/>
    /// as a typed handler.
    /// </summary>
    public sealed class QuickInfoHandler : IRpcRequestHandler<QuickInfoRequest, QuickInfoResponse>
    {
        private readonly TsqlParserService _parserService;
        private readonly QuickInfoProvider _quickInfoProvider;

        public QuickInfoHandler(TsqlParserService parserService, QuickInfoProvider quickInfoProvider)
        {
            _parserService = parserService ?? throw new ArgumentNullException(nameof(parserService));
            _quickInfoProvider = quickInfoProvider ?? throw new ArgumentNullException(nameof(quickInfoProvider));
        }

        public int RequestMessageType => MessageTypes.RequestQuickInfo;
        public int ResponseMessageType => MessageTypes.QuickInfoResult;

        public Task<QuickInfoResponse> HandleAsync(QuickInfoRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var session = ctx.Sessions.GetSession(request.SessionId);
            var documentText = session?.DocumentText ?? string.Empty;
            var cache = session != null
                ? ctx.SchemaCache.GetCache(request.SessionId, session.DatabaseName)
                : null;

            var response = _quickInfoProvider.GetQuickInfo(documentText, request.CursorOffset, cache, _parserService);
            return Task.FromResult(response);
        }
    }
}
