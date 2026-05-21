using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Formatting
{
    // Spec 021 (web edition) -- M0.3 task T013. Nine typed handlers that delegate to the
    // existing FormatRequestHandler. Each replaces a corresponding inline case from
    // NamedPipeTransport's switch and is registered into _pluggableHandlers via TypedHandlerAdapter.
    //
    // BulkFormat / BulkFormatCancel are deliberately NOT migrated here. BulkFormat uses a
    // streaming multi-response dispatch (BulkFormatDispatchAsync) that does not fit the
    // single-request/single-response IRpcRequestHandler<,> shape. Migration of those two
    // requires a streaming-response extension to the transport contract -- tracked as a
    // separate follow-up under spec 021.

    public sealed class FormatDocumentHandler : IRpcRequestHandler<FormatRequest, FormatResponse>
    {
        private readonly FormatRequestHandler _inner;
        public FormatDocumentHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.FormatDocument;
        public int ResponseMessageType => MessageTypes.FormatDocumentResult;
        public Task<FormatResponse> HandleAsync(FormatRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleFormat(request));
    }

    public sealed class FormatSelectionHandler : IRpcRequestHandler<FormatSelectionRequest, FormatSelectionResponse>
    {
        private readonly FormatRequestHandler _inner;
        public FormatSelectionHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.FormatSelection;
        public int ResponseMessageType => MessageTypes.FormatSelectionResult;
        public Task<FormatSelectionResponse> HandleAsync(FormatSelectionRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleFormatSelection(request));
    }

    public sealed class FormatPreviewHandler : IRpcRequestHandler<FormatPreviewRequest, FormatPreviewResponse>
    {
        private readonly FormatRequestHandler _inner;
        public FormatPreviewHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.FormatPreview;
        public int ResponseMessageType => MessageTypes.FormatPreviewResult;
        public Task<FormatPreviewResponse> HandleAsync(FormatPreviewRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleFormatPreview(request));
    }

    public sealed class FormatActionHandler : IRpcRequestHandler<FormatActionRequest, FormatActionResponse>
    {
        private readonly FormatRequestHandler _inner;
        public FormatActionHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.FormatAction;
        public int ResponseMessageType => MessageTypes.FormatActionResult;
        public Task<FormatActionResponse> HandleAsync(FormatActionRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleFormatAction(request));
    }

    /// <summary>
    /// ProfileList is the one format handler whose IPC message carries no payload -- the
    /// shell sends it with a null Payload. <see cref="AllowsEmptyPayload"/> is overridden
    /// to <c>true</c> so <see cref="TypedHandlerAdapter{TReq,TResp}"/> tolerates the null.
    /// </summary>
    public sealed class ProfileListHandler : IRpcRequestHandler<ProfileListRequest, ProfileListResponse>
    {
        private readonly FormatRequestHandler _inner;
        public ProfileListHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.ProfileList;
        public int ResponseMessageType => MessageTypes.ProfileListResult;
        public bool AllowsEmptyPayload => true;
        public Task<ProfileListResponse> HandleAsync(ProfileListRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleProfileList());
    }

    public sealed class ProfileSaveHandler : IRpcRequestHandler<ProfileSaveRequest, ProfileSaveResponse>
    {
        private readonly FormatRequestHandler _inner;
        public ProfileSaveHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.ProfileSave;
        public int ResponseMessageType => MessageTypes.ProfileSaveResult;
        public Task<ProfileSaveResponse> HandleAsync(ProfileSaveRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleProfileSave(request));
    }

    public sealed class ProfileDeleteHandler : IRpcRequestHandler<ProfileDeleteRequest, ProfileDeleteResponse>
    {
        private readonly FormatRequestHandler _inner;
        public ProfileDeleteHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.ProfileDelete;
        public int ResponseMessageType => MessageTypes.ProfileDeleteResult;
        public Task<ProfileDeleteResponse> HandleAsync(ProfileDeleteRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleProfileDelete(request));
    }

    public sealed class ProfileImportHandler : IRpcRequestHandler<ProfileImportRequest, ProfileImportResponse>
    {
        private readonly FormatRequestHandler _inner;
        public ProfileImportHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.ProfileImport;
        public int ResponseMessageType => MessageTypes.ProfileImportResult;
        public Task<ProfileImportResponse> HandleAsync(ProfileImportRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleProfileImport(request));
    }

    public sealed class StyleEditorSchemaHandler : IRpcRequestHandler<StyleEditorSchemaRequest, StyleEditorSchemaResponse>
    {
        private readonly FormatRequestHandler _inner;
        public StyleEditorSchemaHandler(FormatRequestHandler inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        public int RequestMessageType => MessageTypes.RequestStyleEditorSchema;
        public int ResponseMessageType => MessageTypes.StyleEditorSchemaResult;
        public Task<StyleEditorSchemaResponse> HandleAsync(StyleEditorSchemaRequest request, RpcContext ctx, CancellationToken ct)
            => Task.FromResult(_inner.HandleStyleEditorSchema(request));
    }
}
