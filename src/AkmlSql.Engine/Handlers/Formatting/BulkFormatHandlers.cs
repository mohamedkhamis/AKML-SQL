using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Formatting
{
    // Spec 021 (web edition) -- M0.4 wave 3 task T020. Bulk format request/cancel.
    //
    // Despite the original "streaming" comment in the PRD, the bulk-format dispatch is a
    // standard request/response: the engine processes a list of files inside
    // FormatRequestHandler.HandleBulkFormatAsync and returns a single BulkFormatReportResponse
    // at the end. So a typed handler fits without any special streaming machinery.
    //
    // BulkFormatCancel is a notification -- the cancel signal lands on a CancellationTokenSource
    // owned by the FormatRequestHandler.

    public sealed class BulkFormatHandler : IRpcRequestHandler<BulkFormatRequest, BulkFormatReportResponse>
    {
        private readonly FormatRequestHandler _inner;
        public BulkFormatHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.BulkFormat;
        public int ResponseMessageType => MessageTypes.BulkFormatResult;
        public Task<BulkFormatReportResponse> HandleAsync(BulkFormatRequest request, RpcContext ctx, CancellationToken ct)
            => _inner.HandleBulkFormatAsync(request);
    }

    public sealed class BulkFormatCancelHandler : IRpcRequestHandler<BulkFormatCancelRequest, BulkFormatCancelRequest>
    {
        private readonly FormatRequestHandler _inner;
        public BulkFormatCancelHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.BulkFormatCancel;
        public int ResponseMessageType => 0;    // notification, no reply
        public Task<BulkFormatCancelRequest> HandleAsync(BulkFormatCancelRequest request, RpcContext ctx, CancellationToken ct)
        {
            _inner.HandleBulkFormatCancel(request);
            return Task.FromResult(request);
        }
    }
}
