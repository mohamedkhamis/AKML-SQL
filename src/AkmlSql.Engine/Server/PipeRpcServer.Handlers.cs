using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;
using Serilog;

namespace AkmlSql.Engine.Server;

/// <summary>
/// Spec 021 (web edition) -- M0.4 T020 final step. Handler-registration partial. Lifted out of
/// the main <see cref="PipeRpcServer"/> constructor so the transport file stays focused on
/// frame I/O, pipe lifecycle, and dispatch -- not the bulk of per-message-type wiring.
///
/// All references in this file resolve against the engine fields declared in PipeRpcServer.cs
/// (this is a partial class). Closure captures of fields like <c>_historyHandler</c> are
/// lazy: the lambda runs at request time, by which point the field is initialised.
/// </summary>
public partial class PipeRpcServer
{
    private void RegisterPluggableHandlers()
    {
        // Phase 10 (spec 019) US14 FR-080 -- register stub IMessageHandlers for the three
        // spec-014-Phase-2-reserved MessageTypes. Real handlers land in US7 (FindUnusedVariables),
        // US8 (FindInvalidObjects), US11 (EncryptedObjectDecryption); replacing each stub
        // registration with the real handler requires NO change to PipeRpcServer.cs.
        _pluggableHandlers[MessageTypes.FindInvalidObjects] = new Stubs.FindInvalidObjectsHandlerStub();
        _pluggableHandlers[MessageTypes.FindUnusedVariables] = new Stubs.FindUnusedVariablesHandlerStub();
        _pluggableHandlers[MessageTypes.EncryptedObjectDecryption] = new Stubs.EncryptedObjectDecryptionHandlerStub();

        // Spec 021 (web edition) -- M0.2 task T011. RequestCompletion is now served by the
        // typed CompletionHandler under Handlers/Completion/. The legacy switch case at
        // case MessageTypes.RequestCompletion has been removed; the pluggable dispatch path
        // routes the message through TypedHandlerAdapter -> CompletionHandler.HandleAsync.
        _rpcContext = new RpcContext
        {
            Settings = _cachedSettings,
            Sessions = _sessionManager,
            SchemaCache = _schemaCacheManager,
            Logger = Log.Logger,
            ParserService = _parserService,
            SchemaMetadata = _schemaMetadataService,
        };

        var completionHandler = new Handlers.Completion.CompletionHandler(
            _completionEngine,
            () =>
            {
                _cachedSettings ??= Core.Config.ConfigManager.Load();
                _rpcContext.Settings = _cachedSettings;
                return _cachedSettings;
            });
        _pluggableHandlers[MessageTypes.RequestCompletion] =
            new TypedHandlerAdapter<CompletionRequest, CompletionResponse>(completionHandler, _rpcContext);

        // Spec 021 (web edition) -- M0.3 task T013. Nine formatting/profile handlers migrated
        // to the typed contract. BulkFormat / BulkFormatCancel were migrated separately in
        // T020 wave 3 -- the earlier "streaming" caveat turned out to be a misread; bulk
        // format actually fits the standard request/response shape.
        _pluggableHandlers[MessageTypes.FormatDocument] =
            new TypedHandlerAdapter<FormatRequest, FormatResponse>(
                new Handlers.Formatting.FormatDocumentHandler(_formatHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.FormatSelection] =
            new TypedHandlerAdapter<FormatSelectionRequest, FormatSelectionResponse>(
                new Handlers.Formatting.FormatSelectionHandler(_formatHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.FormatPreview] =
            new TypedHandlerAdapter<FormatPreviewRequest, FormatPreviewResponse>(
                new Handlers.Formatting.FormatPreviewHandler(_formatHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.FormatAction] =
            new TypedHandlerAdapter<FormatActionRequest, FormatActionResponse>(
                new Handlers.Formatting.FormatActionHandler(_formatHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.ProfileList] =
            new TypedHandlerAdapter<ProfileListRequest, ProfileListResponse>(
                new Handlers.Formatting.ProfileListHandler(_formatHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.ProfileSave] =
            new TypedHandlerAdapter<ProfileSaveRequest, ProfileSaveResponse>(
                new Handlers.Formatting.ProfileSaveHandler(_formatHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.ProfileDelete] =
            new TypedHandlerAdapter<ProfileDeleteRequest, ProfileDeleteResponse>(
                new Handlers.Formatting.ProfileDeleteHandler(_formatHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.ProfileImport] =
            new TypedHandlerAdapter<ProfileImportRequest, ProfileImportResponse>(
                new Handlers.Formatting.ProfileImportHandler(_formatHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.RequestStyleEditorSchema] =
            new TypedHandlerAdapter<StyleEditorSchemaRequest, StyleEditorSchemaResponse>(
                new Handlers.Formatting.StyleEditorSchemaHandler(_formatHandler), _rpcContext);

        // Spec 021 (web edition) -- M0.3 task T014. RequestAnalyze + AnalysisSettingsChanged.
        var analysisHandler = new Handlers.Analysis.AnalysisHandler(
            _analysisEngine,
            () =>
            {
                _cachedSettings ??= Core.Config.ConfigManager.Load();
                _rpcContext.Settings = _cachedSettings;
                return _cachedSettings;
            });
        _pluggableHandlers[MessageTypes.RequestAnalyze] =
            new TypedHandlerAdapter<CodeAnalysisRequest, CodeAnalysisResponse>(analysisHandler, _rpcContext);

        var analysisSettingsChangedHandler = new Handlers.Analysis.AnalysisSettingsChangedHandler(() =>
        {
            _caSettingsLoader.InvalidateCache();
            _cachedSettings = null;
            _rpcContext.Settings = null;
            _aiHandler.RefreshSettings();
        });
        _pluggableHandlers[MessageTypes.AnalysisSettingsChanged] =
            new TypedHandlerAdapter<AnalysisSettingsChangedRequest, AnalysisSettingsChangedRequest>(
                analysisSettingsChangedHandler, _rpcContext);

        // Spec 021 (web edition) -- M0.3 task T015. Snippet handlers.
        _pluggableHandlers[MessageTypes.SnippetExpand] =
            new TypedHandlerAdapter<SnippetExpandRequest, SnippetExpandResponse>(
                new Handlers.Snippets.SnippetExpandHandler(_snippetHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.SnippetList] =
            new TypedHandlerAdapter<SnippetListRequest, SnippetListResponse>(
                new Handlers.Snippets.SnippetListHandler(_snippetHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.SnippetSave] =
            new TypedHandlerAdapter<SnippetSaveRequest, SnippetSaveResponse>(
                new Handlers.Snippets.SnippetSaveHandler(_snippetHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.SnippetDelete] =
            new TypedHandlerAdapter<SnippetDeleteRequest, SnippetDeleteResponse>(
                new Handlers.Snippets.SnippetDeleteHandler(_snippetHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.SnippetImport] =
            new TypedHandlerAdapter<SnippetImportRequest, SnippetImportResponse>(
                new Handlers.Snippets.SnippetImportHandler(_snippetHandler), _rpcContext);

        // Spec 021 (web edition) -- M0.3 task T016. Refactoring handlers (SwallowCancellation=true).
        _pluggableHandlers[MessageTypes.RequestRefactorPreview] =
            new TypedHandlerAdapter<RefactorPreviewRequest, RefactorPreviewResponse>(
                new Handlers.Refactoring.RefactorPreviewHandler(_refactoringEngine), _rpcContext);
        _pluggableHandlers[MessageTypes.RequestRefactorApply] =
            new TypedHandlerAdapter<RefactorApplyRequest, RefactorApplyResponse>(
                new Handlers.Refactoring.RefactorApplyHandler(_refactoringEngine), _rpcContext);

        // Spec 021 (web edition) -- M0.3 task T017. Schema handlers.
        var schemaRefreshService = new Schema.SchemaRefreshService(
            _sessionManager, _schemaCacheManager, _schemaMetadataService);
        _pluggableHandlers[MessageTypes.SchemaRefreshRequest] =
            new TypedHandlerAdapter<RefreshRequest, RefreshRequest>(
                new Handlers.Schema.SchemaRefreshHandler(schemaRefreshService.Refresh), _rpcContext);
        _pluggableHandlers[MessageTypes.SchemaStatusRequest] =
            new TypedHandlerAdapter<SchemaStatusRequest, SchemaStatusResponse>(
                new Handlers.Schema.SchemaStatusHandler(), _rpcContext);

        // Spec 021 (web edition) -- M5 task T104. SchemaIdentify resolves the canonical
        // (server, database) cache key. The identity-resolver callback parses the session's
        // connection-string Data Source as a stable-enough identity for the initial wiring;
        // a follow-up task swaps in the real "SELECT @@SERVERNAME" query once the engine has
        // shared connection-pooling infrastructure for ad-hoc queries against live sessions.
        _pluggableHandlers[MessageTypes.SchemaIdentifyRequest] =
            new TypedHandlerAdapter<SchemaIdentifyRequest, SchemaIdentifyResponse>(
                new Handlers.Schema.SchemaIdentifyHandler(
                    databaseLookup: sid => _sessionManager.GetSession(sid)?.DatabaseName,
                    identityResolver: sid =>
                    {
                        var session = _sessionManager.GetSession(sid);
                        if (session == null || !session.IsConnected) return null;
                        return Handlers.Schema.SchemaIdentifyHandlerSupport
                            .ParseServerFromConnectionString(session.ConnectionString);
                    }),
                _rpcContext);

        // Spec 021 (web edition) -- M0.3 task T018. Control / lifecycle handlers.
        _pluggableHandlers[MessageTypes.DocumentChanged] =
            new TypedHandlerAdapter<DocumentChange, DocumentChange>(
                new Handlers.Control.DocumentChangedHandler(), _rpcContext);
        _pluggableHandlers[MessageTypes.Ping] =
            new TypedHandlerAdapter<EngineStatusInfo, EngineStatusInfo>(
                new Handlers.Control.PingHandler(), _rpcContext);
        _pluggableHandlers[MessageTypes.Shutdown] =
            new TypedHandlerAdapter<EngineStatusInfo, EngineStatusInfo>(
                new Handlers.Control.ShutdownHandler(), _rpcContext);

        // Spec 021 (web edition) -- M0.5 task T019. AI handlers (thin bridge into
        // _aiHandler.Handle*Async methods that take LookupSession).
        _pluggableHandlers[MessageTypes.AiTextToSql] =
            new Handlers.Ai.AiMessageHandler((msg, ct) => _aiHandler.HandleTextToSqlAsync(msg, LookupSession, ct));
        _pluggableHandlers[MessageTypes.AiExplain] =
            new Handlers.Ai.AiMessageHandler((msg, ct) => _aiHandler.HandleExplainAsync(msg, LookupSession, ct));
        _pluggableHandlers[MessageTypes.AiFix] =
            new Handlers.Ai.AiMessageHandler((msg, ct) => _aiHandler.HandleFixAsync(msg, LookupSession, ct));
        _pluggableHandlers[MessageTypes.AiOptimize] =
            new Handlers.Ai.AiMessageHandler((msg, ct) => _aiHandler.HandleOptimizeAsync(msg, LookupSession, ct));
        _pluggableHandlers[MessageTypes.AiIndexAnalysis] =
            new Handlers.Ai.AiMessageHandler((msg, ct) => _aiHandler.HandleIndexAnalysisAsync(msg, LookupSession, ct));
        _pluggableHandlers[MessageTypes.AiChat] =
            new Handlers.Ai.AiMessageHandler((msg, ct) => _aiHandler.HandleChatAsync(msg, LookupSession, ct));
        _pluggableHandlers[MessageTypes.AiGhostText] =
            new Handlers.Ai.AiMessageHandler((msg, ct) => _aiHandler.HandleGhostTextAsync(msg, LookupSession, ct));
        _pluggableHandlers[MessageTypes.AiProviderTest] =
            new Handlers.Ai.AiMessageHandler((msg, ct) => _aiProviderTestHandler.HandleAsync(msg, ct));

        // Spec 021 (web edition) -- M0.4 wave 1. Lift delegating switch cases.

        // Session-recovery (3 message types, single instance method).
        _pluggableHandlers[MessageTypes.SessionSave] =
            new DelegatingMessageHandler((msg, ct) => _sessionRequestHandler.HandleAsync(msg, MessageTypes.SessionSave));
        _pluggableHandlers[MessageTypes.SessionRestore] =
            new DelegatingMessageHandler((msg, ct) => _sessionRequestHandler.HandleAsync(msg, MessageTypes.SessionRestore));
        _pluggableHandlers[MessageTypes.SessionDelete] =
            new DelegatingMessageHandler((msg, ct) => _sessionRequestHandler.HandleAsync(msg, MessageTypes.SessionDelete));

        // Safety check.
        _pluggableHandlers[MessageTypes.SafetyCheck] =
            new DelegatingMessageHandler((msg, ct) => _safetyCheckHandler.HandleAsync(msg));

        // History (Phase 7) -- _historyHandler is initialised after this method by the
        // PipeRpcServer constructor; closure capture is lazy so the lambdas resolve the
        // field at request time, not registration time.
        _pluggableHandlers[MessageTypes.HistoryRecord] =
            new DelegatingMessageHandler((msg, ct) => _historyHandler.HandleRecordAsync(msg));
        _pluggableHandlers[MessageTypes.HistorySearch] =
            new DelegatingMessageHandler((msg, ct) => _historyHandler.HandleSearchAsync(msg));
        _pluggableHandlers[MessageTypes.HistoryAction] =
            new DelegatingMessageHandler((msg, ct) => _historyHandler.HandleActionAsync(msg));

        // Productivity (Phase 8).
        _pluggableHandlers[MessageTypes.StatementBoundary] =
            new DelegatingMessageHandler((msg, ct) => _productivityHandler.HandleStatementBoundaryAsync(msg));
        _pluggableHandlers[MessageTypes.DocumentOutline] =
            new DelegatingMessageHandler((msg, ct) => _productivityHandler.HandleDocumentOutlineAsync(msg));

        // Navigation (Phase 8) -- needs LookupSession.
        _pluggableHandlers[MessageTypes.GetObjectDefinition] =
            new DelegatingMessageHandler((msg, ct) => _navigationHandler.HandleGetObjectDefinitionAsync(msg, LookupSession, ct));
        _pluggableHandlers[MessageTypes.FindReferences] =
            new DelegatingMessageHandler((msg, ct) => _navigationHandler.HandleFindReferencesAsync(msg, LookupSession, ct));
        _pluggableHandlers[MessageTypes.ObjectSearch] =
            new DelegatingMessageHandler((msg, ct) => _navigationHandler.HandleObjectSearchAsync(msg, LookupSession, ct));

        // CRUD + ScriptAs (Phase 8) -- both need LookupSession.
        _pluggableHandlers[MessageTypes.CrudGeneration] =
            new DelegatingMessageHandler((msg, ct) => _crudGenerationHandler.HandleAsync(msg, LookupSession, ct));
        _pluggableHandlers[MessageTypes.ScriptAs] =
            new DelegatingMessageHandler((msg, ct) => _scriptAsHandler.HandleAsync(msg, LookupSession, ct));

        // Grid export (Phase 8).
        _pluggableHandlers[MessageTypes.GridExport] =
            new DelegatingMessageHandler((msg, ct) => _gridExportService.HandleAsync(msg));

        // Spec 021 (web edition) -- M0.4 wave 2. Completion-family typed handlers.
        _pluggableHandlers[MessageTypes.WildcardExpansion] =
            new TypedHandlerAdapter<WildcardExpansionRequest, WildcardExpansionResponse>(
                new Handlers.Completion.WildcardExpansionHandler(_wildcardHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.RequestSignatureHelp] =
            new TypedHandlerAdapter<SignatureRequest, SignatureResponse>(
                new Handlers.Completion.SignatureHelpHandler(_parserService, _signatureProvider), _rpcContext);
        _pluggableHandlers[MessageTypes.RequestQuickInfo] =
            new TypedHandlerAdapter<QuickInfoRequest, QuickInfoResponse>(
                new Handlers.Completion.QuickInfoHandler(_parserService, _quickInfoProvider), _rpcContext);

        // Spec 021 (web edition) -- M0.4 wave 3. Final three migrations -- closes out T020.
        _pluggableHandlers[MessageTypes.ConnectionChanged] =
            new TypedHandlerAdapter<ConnectionInfo, ConnectionInfo>(
                new Handlers.Control.ConnectionChangedHandler(), _rpcContext);
        _pluggableHandlers[MessageTypes.BulkFormat] =
            new TypedHandlerAdapter<BulkFormatRequest, BulkFormatReportResponse>(
                new Handlers.Formatting.BulkFormatHandler(_formatHandler), _rpcContext);
        _pluggableHandlers[MessageTypes.BulkFormatCancel] =
            new TypedHandlerAdapter<BulkFormatCancelRequest, BulkFormatCancelRequest>(
                new Handlers.Formatting.BulkFormatCancelHandler(_formatHandler), _rpcContext);
    }
}
