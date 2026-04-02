using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Formatter;
using AkmlSql.Engine.History;
using AkmlSql.Engine.Export;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Navigation;
using AkmlSql.Engine.Productivity;
using AkmlSql.Engine.Safety;
using AkmlSql.Engine.Sessions;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Schema;
using AkmlSql.Formatting.Profiles;
using MessagePack;
using Serilog;
// ReSharper disable MethodSupportsCancellation
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
#pragma warning disable CA1416

namespace AkmlSql.Engine.Server;

[SuppressMessage("ReSharper", "UnusedParameter.Local")]
public class PipeRpcServer
{
    private readonly string _pipeName;
    private Core.Config.AppSettings? _cachedSettings;
    private readonly SessionManager _sessionManager = new();
    private readonly TsqlParserService _parserService = new();
    private readonly CompletionEngine _completionEngine;
    private readonly WildcardExpansionHandler _wildcardHandler;
    private readonly SchemaCacheManager _schemaCacheManager = new();
    private readonly SchemaMetadataService _schemaMetadataService = new();
    private readonly SignatureProvider _signatureProvider = new();
    private readonly QuickInfoProvider _quickInfoProvider = new();
    private readonly FormatRequestHandler _formatHandler;
    private readonly SnippetRequestHandler _snippetHandler;
    private readonly AnalysisEngine _analysisEngine;
    private readonly CaSettingsLoader _caSettingsLoader = new();
    private readonly RefactoringEngine _refactoringEngine;
    private readonly HistoryRequestHandler _historyHandler;
    private readonly HistoryRetentionService _historyRetentionService;
    private readonly SessionRequestHandler _sessionRequestHandler = new();
    private readonly SafetyCheckHandler _safetyCheckHandler;
    private readonly ProductivityRequestHandler _productivityHandler;
    private readonly NavigationRequestHandler _navigationHandler;
    private readonly GridExportService _gridExportService = new();
    private readonly CrudGenerationHandler _crudGenerationHandler;
    private readonly ScriptAsHandler _scriptAsHandler;
    private readonly AiRequestHandler _aiHandler;
    private readonly AiProviderTestHandler _aiProviderTestHandler = new();

    public PipeRpcServer(string pipeName)
    {
        _pipeName = pipeName;
        _completionEngine = new CompletionEngine(_parserService);
        _wildcardHandler = new WildcardExpansionHandler(_parserService);
        _formatHandler = new FormatRequestHandler(ProfileManager.CreateDefault());
        _analysisEngine = new AnalysisEngine(_parserService, new RuleRegistry(), _caSettingsLoader);
        _refactoringEngine = new RefactoringEngine(_parserService, _schemaCacheManager);

        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AKML SQL");
        var personalSnippets = Path.Combine(appDataFolder, "snippets", "personal");
        var builtInSnippets = Path.Combine(AppContext.BaseDirectory, "snippets");
        _snippetHandler = new SnippetRequestHandler(personalSnippets, builtInSnippets);
        _safetyCheckHandler = new SafetyCheckHandler(_parserService);
        _productivityHandler = new ProductivityRequestHandler(_parserService);
        _navigationHandler = new NavigationRequestHandler(_schemaCacheManager);
        _crudGenerationHandler = new CrudGenerationHandler(_schemaCacheManager);
        _scriptAsHandler = new ScriptAsHandler(_schemaCacheManager);
        _aiHandler = new AiRequestHandler(_schemaCacheManager, _parserService);

        // History: initialize database and retention service
        var historyDb = new HistoryDatabase();
        _historyHandler = new HistoryRequestHandler(historyDb);
        var settings = Core.Config.ConfigManager.Load();
        _historyRetentionService = new HistoryRetentionService(historyDb, settings.History);
        if (settings.History.Enabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await historyDb.InitializeAsync();
                    await _historyRetentionService.StartAsync();
                    Log.Information("History database and retention service started");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to initialize history database");
                }
            });
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipeSecurity = CreatePipeSecurity();
            await using var pipe = NamedPipeServerStreamAcl.Create(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 65536,
                outBufferSize: 65536,
                pipeSecurity);

            Log.Information("Waiting for client connection on pipe {Pipe}", _pipeName);
            await pipe.WaitForConnectionAsync(ct);
            Log.Information("Client connected.");

            try
            {
                await HandleClientAsync(pipe, ct);
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Client disconnected (pipe broken).");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error handling client.");
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            var message = await FrameProtocol.ReadFramedAsync(pipe, ct);
            if (message == null)
            {
                break;
            }

            var response = await DispatchAsync(message, ct);
            if (response != null)
            {
                await FrameProtocol.WriteFramedAsync(pipe, response, ct);
            }
        }
    }

    private Task<RpcMessage?> DispatchAsync(RpcMessage message, CancellationToken ct)
    {
        try
        {
            switch (message.MessageType)
            {
                case MessageTypes.ConnectionChanged:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var connInfo = MessagePackSerializer.Deserialize<ConnectionInfo>(message.Payload);
                    _sessionManager.UpdateSession(connInfo);
                    _parserService.SetServerVersion(connInfo.ServerVersion);
                    Log.Information("Connection changed: {Session} -> {Db}", connInfo.SessionId, connInfo.DatabaseName);

                    // Fire-and-forget: populate schema cache Phase A
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var cache = _schemaCacheManager.GetOrCreateCache(
                                connInfo.SessionId, connInfo.DatabaseName);
                            if (cache.Phase == PopulationPhase.NotLoaded)
                            {
                                Log.Information("Starting Phase A schema population for {Db}", connInfo.DatabaseName);
                                await _schemaMetadataService.PopulatePhaseAAsync(
                                    cache, connInfo.ConnectionString, CancellationToken.None);
                                _schemaCacheManager.EvictLru();

                                // Phase B: load columns, FKs, parameters in background
                                // Required for JOIN completions and column suggestions
                                if (cache.Phase == PopulationPhase.PhaseA)
                                {
                                    Log.Information("Starting Phase B for {Db}", connInfo.DatabaseName);
                                    await _schemaMetadataService.PopulatePhaseBAsync(
                                        cache, connInfo.ConnectionString, CancellationToken.None);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Background Phase A population failed for {Db}", connInfo.DatabaseName);
                        }
                    });
                    return Task.FromResult<RpcMessage?>(null); // notification, no response

                case MessageTypes.DocumentChanged:
                    if (message.Payload == null)
                    {
                        return Task.FromResult<RpcMessage?>(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var docChange = MessagePackSerializer.Deserialize<DocumentChange>(message.Payload);
                    _sessionManager.UpdateDocument(docChange);
                    return Task.FromResult<RpcMessage?>(null);

                case MessageTypes.RequestCompletion:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var compReq = MessagePackSerializer.Deserialize<CompletionRequest>(message.Payload);
                    var session = _sessionManager.GetSession(compReq.SessionId);
                    var documentText = session?.DocumentText ?? string.Empty;
                    var dbCache = session != null
                        ? _schemaCacheManager.GetCache(compReq.SessionId, session.DatabaseName)
                        : null;
                    var compResp = _completionEngine.GetCompletions(documentText, compReq.CursorOffset, dbCache);
                    return Task.FromResult(CreateResponse(MessageTypes.CompletionResult, message.RequestId, compResp));

                case MessageTypes.WildcardExpansion:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var wcReq = MessagePackSerializer.Deserialize<WildcardExpansionRequest>(message.Payload);
                    var wcSession = _sessionManager.GetSession(wcReq.SessionId);
                    var wcCache = wcSession != null
                        ? _schemaCacheManager.GetCache(wcReq.SessionId, wcSession.DatabaseName)
                        : null;
                    var wcResp = _wildcardHandler.Handle(
                        wcReq.DocumentText, wcReq.CursorOffset, wcReq.Qualifier, wcCache);
                    return Task.FromResult(CreateResponse(MessageTypes.WildcardExpansionResult, message.RequestId, wcResp));

                case MessageTypes.RequestSignatureHelp:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var sigReq = MessagePackSerializer.Deserialize<SignatureRequest>(message.Payload);
                    var sigSession = _sessionManager.GetSession(sigReq.SessionId);
                    var sigText = sigSession?.DocumentText ?? string.Empty;
                    var sigCache = sigSession != null
                        ? _schemaCacheManager.GetCache(sigReq.SessionId, sigSession.DatabaseName)
                        : null;
                    // Extract function name and parameter index from the document text at cursor
                    var sigTokens = _parserService.GetTokenStream(sigText);
                    var (funcName, parenOffset) = FindFunctionAtCursor(sigTokens, sigReq.CursorOffset);
                    var paramIdx = parenOffset >= 0
                        ? SignatureProvider.CountCommasBeforeCursor(sigTokens, sigReq.CursorOffset, parenOffset)
                        : 0;
                    var sigResp = _signatureProvider.GetSignature(funcName, paramIdx, sigCache);
                    return Task.FromResult(CreateResponse(MessageTypes.SignatureHelpResult, message.RequestId, sigResp));

                case MessageTypes.RequestQuickInfo:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var qiReq = MessagePackSerializer.Deserialize<QuickInfoRequest>(message.Payload);
                    var qiSession = _sessionManager.GetSession(qiReq.SessionId);
                    var qiText = qiSession?.DocumentText ?? string.Empty;
                    var qiCache = qiSession != null
                        ? _schemaCacheManager.GetCache(qiReq.SessionId, qiSession.DatabaseName)
                        : null;
                    var qiResp = _quickInfoProvider.GetQuickInfo(qiText, qiReq.CursorOffset, qiCache, _parserService);
                    return Task.FromResult(CreateResponse(MessageTypes.QuickInfoResult, message.RequestId, qiResp));

                case MessageTypes.SchemaRefreshRequest:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var refreshReq = MessagePackSerializer.Deserialize<RefreshRequest>(message.Payload);
                    var refreshSession = !string.IsNullOrEmpty(refreshReq.SessionId)
                        ? _sessionManager.GetSession(refreshReq.SessionId) : null;
                    int refreshedCount = 0;
                    if (refreshSession != null)
                    {
                        var refCache = _schemaCacheManager.GetCache(refreshReq.SessionId, refreshSession.DatabaseName);
                        if (refCache != null)
                        {
                            refCache.IsStale = true;
                            refreshedCount = refCache.Schemas.Values.Sum(s => s.Objects.Count);
                        }
                    }
                    var refResp = new RefreshResponse { Success = true, ObjectCount = refreshedCount };
                    return Task.FromResult(CreateResponse(MessageTypes.SchemaRefreshComplete, message.RequestId, refResp));

                case MessageTypes.Ping:
                    var status = new EngineStatusInfo
                    {
                        MemoryUsageMb = (int)(GC.GetTotalMemory(false) / (1024 * 1024)),
                        CachedDatabases = _schemaCacheManager.CacheCount,
                        ActiveSessions = _sessionManager.SessionCount,
                        UptimeSeconds = 0
                    };
                    return Task.FromResult(CreateResponse(MessageTypes.Pong, message.RequestId, status));

                case MessageTypes.FormatDocument:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var fmtReq = MessagePackSerializer.Deserialize<FormatRequest>(message.Payload);
                    var fmtResp = _formatHandler.HandleFormat(fmtReq);
                    return Task.FromResult(CreateResponse(MessageTypes.FormatDocumentResult, message.RequestId, fmtResp));

                case MessageTypes.FormatSelection:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var fmtSelReq = MessagePackSerializer.Deserialize<FormatSelectionRequest>(message.Payload);
                    var fmtSelResp = _formatHandler.HandleFormatSelection(fmtSelReq);
                    return Task.FromResult(CreateResponse(MessageTypes.FormatSelectionResult, message.RequestId, fmtSelResp));

                case MessageTypes.FormatPreview:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var fmtPrevReq = MessagePackSerializer.Deserialize<FormatPreviewRequest>(message.Payload);
                    var fmtPrevResp = _formatHandler.HandleFormatPreview(fmtPrevReq);
                    return Task.FromResult(CreateResponse(MessageTypes.FormatPreviewResult, message.RequestId, fmtPrevResp));

                case MessageTypes.FormatAction:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var fmtActReq = MessagePackSerializer.Deserialize<FormatActionRequest>(message.Payload);
                    var fmtActResp = _formatHandler.HandleFormatAction(fmtActReq);
                    return Task.FromResult(CreateResponse(MessageTypes.FormatActionResult, message.RequestId, fmtActResp));

                case MessageTypes.ProfileList:
                    var profListResp = _formatHandler.HandleProfileList();
                    return Task.FromResult(CreateResponse(MessageTypes.ProfileListResult, message.RequestId, profListResp));

                case MessageTypes.ProfileSave:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var profSaveReq = MessagePackSerializer.Deserialize<ProfileSaveRequest>(message.Payload);
                    var profSaveResp = _formatHandler.HandleProfileSave(profSaveReq);
                    return Task.FromResult(CreateResponse(MessageTypes.ProfileSaveResult, message.RequestId, profSaveResp));

                case MessageTypes.ProfileDelete:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var profDelReq = MessagePackSerializer.Deserialize<ProfileDeleteRequest>(message.Payload);
                    var profDelResp = _formatHandler.HandleProfileDelete(profDelReq);
                    return Task.FromResult(CreateResponse(MessageTypes.ProfileDeleteResult, message.RequestId, profDelResp));

                case MessageTypes.ProfileImport:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var profImpReq = MessagePackSerializer.Deserialize<ProfileImportRequest>(message.Payload);
                    var profImpResp = _formatHandler.HandleProfileImport(profImpReq);
                    return Task.FromResult(CreateResponse(MessageTypes.ProfileImportResult, message.RequestId, profImpResp));

                case MessageTypes.BulkFormat:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var bulkReq = MessagePackSerializer.Deserialize<BulkFormatRequest>(message.Payload);
                    return BulkFormatDispatchAsync(bulkReq, message.RequestId);

                case MessageTypes.BulkFormatCancel:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var bulkCancelReq = MessagePackSerializer.Deserialize<BulkFormatCancelRequest>(message.Payload);
                    _formatHandler.HandleBulkFormatCancel(bulkCancelReq);
                    return Task.FromResult<RpcMessage?>(null); // notification, no response

                case MessageTypes.SnippetExpand:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var snipExpandReq = MessagePackSerializer.Deserialize<SnippetExpandRequest>(message.Payload);
                    var snipSession = _sessionManager.GetSession(snipExpandReq.SessionId);
                    var snipExpandResp = _snippetHandler.HandleExpand(
                        snipExpandReq, snipSession?.DatabaseName);
                    return Task.FromResult(CreateResponse(MessageTypes.SnippetExpandResult, message.RequestId, snipExpandResp));

                case MessageTypes.SnippetList:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var snipListReq = MessagePackSerializer.Deserialize<SnippetListRequest>(message.Payload);
                    var snipListResp = _snippetHandler.HandleList(snipListReq);
                    return Task.FromResult(CreateResponse(MessageTypes.SnippetListResult, message.RequestId, snipListResp));

                case MessageTypes.SnippetSave:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var snipSaveReq = MessagePackSerializer.Deserialize<SnippetSaveRequest>(message.Payload);
                    var snipSaveResp = _snippetHandler.HandleSave(snipSaveReq);
                    return Task.FromResult(CreateResponse(MessageTypes.SnippetSaveResult, message.RequestId, snipSaveResp));

                case MessageTypes.SnippetDelete:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var snipDelReq = MessagePackSerializer.Deserialize<SnippetDeleteRequest>(message.Payload);
                    var snipDelResp = _snippetHandler.HandleDelete(snipDelReq);
                    return Task.FromResult(CreateResponse(MessageTypes.SnippetDeleteResult, message.RequestId, snipDelResp));

                case MessageTypes.SnippetImport:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var snipImpReq = MessagePackSerializer.Deserialize<SnippetImportRequest>(message.Payload);
                    var snipImpResp = _snippetHandler.HandleImport(snipImpReq);
                    return Task.FromResult(CreateResponse(MessageTypes.SnippetImportResult, message.RequestId, snipImpResp));

                case MessageTypes.RequestAnalyze:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var analyzeReq = MessagePackSerializer.Deserialize<CodeAnalysisRequest>(message.Payload);
                    return AnalyzeAsync(analyzeReq, message.RequestId, ct);

                case MessageTypes.AnalysisSettingsChanged:
                    _caSettingsLoader.InvalidateCache();
                    _cachedSettings = null; // Invalidate cached AppSettings
                    _aiHandler.RefreshSettings();
                    return Task.FromResult<RpcMessage?>(null);

                case MessageTypes.RequestRefactorPreview:
                    if (message.Payload == null)
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    var rfPrevReq = MessagePackSerializer.Deserialize<RefactorPreviewRequest>(message.Payload);
                    return RefactorPreviewAsync(rfPrevReq, message.RequestId, ct);

                case MessageTypes.RequestRefactorApply:
                    if (message.Payload == null)
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    var rfApplyReq = MessagePackSerializer.Deserialize<RefactorApplyRequest>(message.Payload);
                    return RefactorApplyAsync(rfApplyReq, message.RequestId, ct);

                case MessageTypes.SessionSave:
                case MessageTypes.SessionRestore:
                case MessageTypes.SessionDelete:
                    return _sessionRequestHandler.HandleAsync(message, message.MessageType);

                case MessageTypes.SafetyCheck:
                    return _safetyCheckHandler.HandleAsync(message);

                case MessageTypes.HistoryRecord:
                    return _historyHandler.HandleRecordAsync(message);

                case MessageTypes.HistorySearch:
                    return _historyHandler.HandleSearchAsync(message);

                case MessageTypes.HistoryAction:
                    return _historyHandler.HandleActionAsync(message);

                case MessageTypes.StatementBoundary:
                    return _productivityHandler.HandleStatementBoundaryAsync(message);

                case MessageTypes.DocumentOutline:
                    return _productivityHandler.HandleDocumentOutlineAsync(message);

                case MessageTypes.GetObjectDefinition:
                    return _navigationHandler.HandleGetObjectDefinitionAsync(message, LookupSession, ct);

                case MessageTypes.FindReferences:
                    return _navigationHandler.HandleFindReferencesAsync(message, LookupSession, ct);

                case MessageTypes.ObjectSearch:
                    return _navigationHandler.HandleObjectSearchAsync(message, LookupSession, ct);

                case MessageTypes.CrudGeneration:
                    return _crudGenerationHandler.HandleAsync(message, LookupSession, ct);

                case MessageTypes.ScriptAs:
                    return _scriptAsHandler.HandleAsync(message, LookupSession, ct);

                case MessageTypes.GridExport:
                    return _gridExportService.HandleAsync(message);

                // Phase 9: AI Assistance
                case MessageTypes.AiTextToSql:
                    return _aiHandler.HandleTextToSqlAsync(message, LookupSession, ct);
                case MessageTypes.AiExplain:
                    return _aiHandler.HandleExplainAsync(message, LookupSession, ct);
                case MessageTypes.AiFix:
                    return _aiHandler.HandleFixAsync(message, LookupSession, ct);
                case MessageTypes.AiOptimize:
                    return _aiHandler.HandleOptimizeAsync(message, LookupSession, ct);
                case MessageTypes.AiIndexAnalysis:
                    return _aiHandler.HandleIndexAnalysisAsync(message, LookupSession, ct);
                case MessageTypes.AiChat:
                    return _aiHandler.HandleChatAsync(message, LookupSession, ct);
                case MessageTypes.AiGhostText:
                    return _aiHandler.HandleGhostTextAsync(message, LookupSession, ct);
                case MessageTypes.AiProviderTest:
                    return _aiProviderTestHandler.HandleAsync(message, ct);

                case MessageTypes.Shutdown:
                    Log.Information("Shutdown requested by client.");
                    throw new OperationCanceledException("Shutdown requested");

                default:
                    Log.Warning("Unknown message type: {Type}", message.MessageType);
                    return Task.FromResult<RpcMessage?>(null);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error dispatching message type {Type}", message.MessageType);
            var errorInfo = new ErrorInfo { Code = -1, Message = ex.Message };
            return Task.FromResult(CreateResponse(MessageTypes.Error, message.RequestId, errorInfo));
        }
    }

    /// <summary>
    /// Walks backwards from cursorOffset to find the function name before the nearest '('.
    /// Returns (functionName, openParenOffset) or ("", -1) if not found.
    /// </summary>
    private static (string FunctionName, int ParenOffset) FindFunctionAtCursor(
        IList<Microsoft.SqlServer.TransactSql.ScriptDom.TSqlParserToken> tokens, int cursorOffset)
    {
        // Find the nearest open paren before cursor at the current nesting level
        int depth = 0;
        int parenTokenIndex = -1;
        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (t.Offset >= cursorOffset)
            {
                continue;
            }

            if (t.TokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.RightParenthesis)
            {
                depth++;
            }
            else if (t.TokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.LeftParenthesis)
            {
                if (depth == 0)
                {
                    parenTokenIndex = i;
                    break;
                }
                depth--;
            }
        }

        if (parenTokenIndex <= 0)
        {
            return (string.Empty, -1);
        }

        // Walk back from paren to find the identifier (function name)
        for (int i = parenTokenIndex - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (t.TokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.WhiteSpace)
            {
                continue;
            }

            if (t.TokenType is Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Identifier or Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.QuotedIdentifier)
            {
                return (t.Text.Trim('[', ']', '"'), tokens[parenTokenIndex].Offset);
            }
            // Could be a keyword-function like CONVERT, CAST
            return (t.Text, tokens[parenTokenIndex].Offset);
        }

        return (string.Empty, -1);
    }

    private async Task<RpcMessage?> BulkFormatDispatchAsync(BulkFormatRequest req, int requestId)
    {
        var bulkResp = await _formatHandler.HandleBulkFormatAsync(req);
        return CreateResponse(MessageTypes.BulkFormatResult, requestId, bulkResp);
    }

    private async Task<RpcMessage?> RefactorPreviewAsync(RefactorPreviewRequest req, int requestId, CancellationToken ct)
    {
        try
        {
            var response = await _refactoringEngine.PreviewAsync(req, _sessionManager, ct);
            return CreateResponse(MessageTypes.RefactorPreviewResult, requestId, response);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RefactorPreview failed");
            return CreateErrorResponse("Refactor preview failed: " + ex.Message, requestId);
        }
    }

    private async Task<RpcMessage?> RefactorApplyAsync(RefactorApplyRequest req, int requestId, CancellationToken ct)
    {
        try
        {
            var response = await _refactoringEngine.ApplyAsync(req, ct);
            return CreateResponse(MessageTypes.RefactorApplyResult, requestId, response);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RefactorApply failed");
            return CreateErrorResponse("Refactor apply failed: " + ex.Message, requestId);
        }
    }

    private async Task<RpcMessage?> AnalyzeAsync(CodeAnalysisRequest req, int requestId, CancellationToken ct)
    {
        try
        {
            _cachedSettings ??= Core.Config.ConfigManager.Load();
            var globalSettings = _cachedSettings.CodeAnalysis;
            var response = await _analysisEngine.AnalyzeAsync(
                req, _sessionManager, _schemaCacheManager, globalSettings, ct);
            return CreateResponse(MessageTypes.AnalysisResult, requestId, response);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Analysis failed for session {Session}", req.SessionId);
            return CreateErrorResponse("Analysis failed: " + ex.Message, requestId);
        }
    }

    /// <summary>
    /// Session lookup delegate passed to NavigationRequestHandler.
    /// Returns (ConnectionString, DatabaseName) for the given session ID.
    /// </summary>
    private (string? ConnectionString, string? DatabaseName) LookupSession(string sessionId)
    {
        var session = _sessionManager.GetSession(sessionId);
        if (session == null || !session.IsConnected)
            return (null, null);
        return (session.ConnectionString, session.DatabaseName);
    }

    private static RpcMessage CreateErrorResponse(string message, int requestId)
    {
        var errorInfo = new ErrorInfo { Code = -1, Message = message };
        return CreateResponse(MessageTypes.Error, requestId, errorInfo);
    }

    private static RpcMessage CreateResponse<T>(int messageType, int requestId, T payload)
    {
        return new RpcMessage
        {
            MessageType = messageType,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(payload)
        };
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
            security.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        return security;
    }
}
