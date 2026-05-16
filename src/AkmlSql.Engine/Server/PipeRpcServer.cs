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
public partial class PipeRpcServer
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

    // Phase 10 (spec 019) US14 FR-080 — hybrid dispatch table. Future
    // MessageType integers register an IMessageHandler here in the constructor
    // and DispatchAsync routes them via dictionary lookup *before* falling
    // through to the existing 53-case switch. Existing switch cases are left
    // unchanged to keep the test gate green; they can be migrated incrementally
    // in later sessions if desired.
    private readonly Dictionary<int, IMessageHandler> _pluggableHandlers = new();

    // Spec 021 (web edition) — M0.2. RpcContext carried alongside the legacy fields so the
    // migrated typed handlers (registered via TypedHandlerAdapter into _pluggableHandlers) see
    // the same settings / sessions / schema cache the legacy switch consults. Assigned by
    // RegisterPluggableHandlers in the partial file PipeRpcServer.Handlers.cs (cannot be
    // readonly because partial-class methods are not considered "in the constructor" by C#).
    private RpcContext _rpcContext = null!;

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

        // Wire SessionTrackerBridge so completion providers (e.g. DatabaseProvider)
        // can look up the active connection string for a given session without
        // holding a direct reference to SessionManager.
        Completion.Providers.SessionTrackerBridge.Configure(sessionId =>
        {
            var s = _sessionManager.GetSession(sessionId);
            if (s == null) return null;
            string server = string.Empty;
            try
            {
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(s.ConnectionString);
                server = builder.DataSource ?? string.Empty;
            }
            catch { /* ignore — fallback to empty server name */ }
            return new Completion.Providers.ConnectionLookupResult
            {
                ConnectionString = s.ConnectionString,
                DatabaseName = s.DatabaseName,
                ServerName = server
            };
        });

        // Spec 021 (web edition) -- M0.4 task T020 final step. All ~50 message types are
        // registered through this single call; the implementation lives in the partial file
        // PipeRpcServer.Handlers.cs so the transport file stays focused on frame I/O.
        RegisterPluggableHandlers();

        // (Original handler-registration block moved to PipeRpcServer.Handlers.cs.)

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
            // Phase 10 (spec 019) US14 FR-080 — hybrid dispatch: consult the
            // pluggable handler dictionary first. On miss, fall through to the
            // existing switch. Future MessageType additions register a handler
            // and require zero changes to this method.
            //
            // Routed through DispatchPluggableAsync (vs. .ContinueWith(t => t.Result))
            // so a thrown exception inside a handler propagates as itself, not
            // wrapped in AggregateException — the outer try/catch then sees the
            // original exception type for accurate logging.
            if (_pluggableHandlers.TryGetValue(message.MessageType, out var pluggable))
            {
                return DispatchPluggableAsync(pluggable, message, ct);
            }

            switch (message.MessageType)
            {
                // case MessageTypes.ConnectionChanged: migrated to Handlers/Control/ConnectionChangedHandler.cs (spec 021 T020 wave 3).

                // case MessageTypes.DocumentChanged: migrated to Handlers/Control/ (spec 021 T018).

                // case MessageTypes.RequestCompletion: migrated to Handlers/Completion/CompletionHandler.cs
                // via TypedHandlerAdapter registered in _pluggableHandlers (spec 021 T011).
                // The hybrid dispatch at the top of this method picks it up before this switch
                // is consulted, so reaching this point for RequestCompletion is unreachable.

                // case MessageTypes.WildcardExpansion, RequestSignatureHelp, RequestQuickInfo:
                // migrated to Handlers/Completion/ (spec 021 T020 wave 2).

                // case MessageTypes.SchemaRefreshRequest, SchemaStatusRequest, Ping:
                // migrated to Handlers/Schema/ and Handlers/Control/ (spec 021 T017, T018).

                // case MessageTypes.FormatDocument, FormatSelection, FormatPreview, FormatAction,
                // ProfileList, ProfileSave, ProfileDelete, ProfileImport, RequestStyleEditorSchema:
                // migrated to Handlers/Formatting/FormattingHandlers.cs and registered via
                // TypedHandlerAdapter in _pluggableHandlers (spec 021 T013). The hybrid dispatch
                // at the top of this method picks them up before the switch is consulted.

                // case MessageTypes.BulkFormat, BulkFormatCancel: migrated to Handlers/Formatting/BulkFormatHandlers.cs (spec 021 T020 wave 3).

                // case MessageTypes.SnippetExpand, SnippetList, SnippetSave, SnippetDelete, SnippetImport:
                // migrated to Handlers/Snippets/SnippetHandlers.cs (spec 021 T015).

                // case MessageTypes.RequestAnalyze, AnalysisSettingsChanged:
                // migrated to Handlers/Analysis/AnalysisHandlers.cs and registered via
                // TypedHandlerAdapter in _pluggableHandlers (spec 021 T014).

                // case MessageTypes.RequestRefactorPreview, RequestRefactorApply:
                // migrated to Handlers/Refactoring/RefactoringHandlers.cs (spec 021 T016).

                // 14 delegating cases (SessionSave/Restore/Delete, SafetyCheck, History x3,
                // StatementBoundary, DocumentOutline, GetObjectDefinition, FindReferences,
                // ObjectSearch, CrudGeneration, ScriptAs, GridExport) migrated to
                // DelegatingMessageHandler entries in _pluggableHandlers (spec 021 T020 wave 1).

                // Phase 9: AI Assistance -- 8 message types (AiTextToSql, AiExplain, AiFix,
                // AiOptimize, AiIndexAnalysis, AiChat, AiGhostText, AiProviderTest) migrated to
                // Handlers/Ai/AiMessageHandlers.cs and registered via the AiMessageHandler
                // bridge in _pluggableHandlers (spec 021 T019).

                // Spec 014 stubs (FindInvalidObjects / FindUnusedVariables /
                // EncryptedObjectDecryption) migrated to IMessageHandler stubs
                // registered in the constructor — see _pluggableHandlers above.
                // Per Phase 10 (spec 019) US14 FR-080 hybrid dispatch.

                // case MessageTypes.Shutdown: migrated to Handlers/Control/ShutdownHandler (spec 021 T018).
                // The migrated handler still throws OperationCanceledException to tear down the
                // pipe loop; the outer OCE catch below re-throws so RunAsync exits as before.

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
            return Task.FromResult<RpcMessage?>(RpcResponseFactory.CreateErrorResponse(ex.Message, message.RequestId));
        }
    }


    // The former BulkFormatDispatchAsync, RefactorPreviewAsync, RefactorApplyAsync, and
    // AnalyzeAsync private methods have all been migrated to typed handlers under
    // Handlers/{Formatting, Refactoring, Analysis}/ (spec 021 T013/T014/T016/T020).

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


    // Phase 10 (spec 019) US14 FR-080: hybrid pluggable dispatch wrapper.
    // Routes the handler call through an awaited helper rather than
    // .ContinueWith(t => t.Result) so a thrown exception inside the handler
    // propagates as itself (not wrapped in AggregateException).
    private static async Task<RpcMessage?> DispatchPluggableAsync(
        IMessageHandler handler, RpcMessage message, CancellationToken ct)
    {
        return await handler.HandleAsync(message, ct).ConfigureAwait(false);
    }

    // Spec 021 (web edition) T020 finishing: the response factories moved to
    // AkmlSql.Engine.RpcResponseFactory. Handlers + adapters call those statics directly;
    // PipeRpcServer no longer carries them.

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
