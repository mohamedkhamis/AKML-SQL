using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Refactoring
{
    // Spec 021 (web edition) -- M0.3 task T016. Two typed handlers wrapping the existing
    // RefactoringEngine. Both opt in to SwallowCancellation=true so a cancelled refactor
    // (typing supersedes an in-flight preview) does not propagate OCE to the outer
    // DispatchAsync (which would tear down the pipe loop).
    //
    // The prior inline RefactorPreviewAsync/RefactorApplyAsync caught all Exception types
    // (including OCE) and converted them to an Error response. We slightly tighten that:
    // OCE now produces no response (matches Analysis handler's behaviour); other exceptions
    // bubble to NamedPipeTransport.DispatchAsync's outer catch which writes an Error frame.

    public sealed class RefactorPreviewHandler : IRpcRequestHandler<RefactorPreviewRequest, RefactorPreviewResponse>
    {
        private readonly RefactoringEngine _engine;

        public RefactorPreviewHandler(RefactoringEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public int RequestMessageType => MessageTypes.RequestRefactorPreview;
        public int ResponseMessageType => MessageTypes.RefactorPreviewResult;
        public bool SwallowCancellation => true;

        public Task<RefactorPreviewResponse> HandleAsync(RefactorPreviewRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            return _engine.PreviewAsync(request, ctx.Sessions, ct);
        }
    }

    public sealed class RefactorApplyHandler : IRpcRequestHandler<RefactorApplyRequest, RefactorApplyResponse>
    {
        private readonly RefactoringEngine _engine;

        public RefactorApplyHandler(RefactoringEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public int RequestMessageType => MessageTypes.RequestRefactorApply;
        public int ResponseMessageType => MessageTypes.RefactorApplyResult;
        public bool SwallowCancellation => true;

        public Task<RefactorApplyResponse> HandleAsync(RefactorApplyRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            return _engine.ApplyAsync(request, ct);
        }
    }
}
