using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Export;
using AkmlSql.Engine.Formatter;
using AkmlSql.Engine.History;
using AkmlSql.Engine.Navigation;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Productivity;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Safety;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Sessions;
using AkmlSql.Engine.Snippets;
using AkmlSql.Formatting.Profiles;
using Serilog;
#pragma warning disable CA1416

namespace AkmlSql.Engine;

/// <summary>
/// Spec 022 (M0 closure) -- P2 / US2. Composition root for shell-to-engine handlers.
/// Builds every engine service, registers every <see cref="Core.Ipc.MessageTypes"/> with the
/// supplied <see cref="RpcRouter"/>, and -- when history is enabled -- starts the
/// <see cref="HistoryRetentionService"/> on a background task. The retention service is also
/// returned so callers can hold a handle to it.
///
/// <para>Lifted out of the partial-class <c>PipeRpcServer.Handlers.cs</c> so the transport file
/// stays focused on frame I/O and lifecycle. All three transports (named-pipe, in-process,
/// WebSocket) consume the same composition output via <see cref="EngineComposition.Build"/>.</para>
/// </summary>
internal static class EngineHandlerRegistry
{
    public static HistoryRetentionService RegisterAllHandlers(
        RpcRouter router, RpcContext ctx, Handlers.Handshake.HandshakeHandler? handshakeHandler = null)
    {
        // === Services scoped to this method; handlers capture by closure. ===
        var sessions = ctx.Sessions;
        var parser = ctx.ParserService ?? new TsqlParserService();
        var schemaCache = ctx.SchemaCache;
        var schemaMeta = ctx.SchemaMetadata ?? new SchemaMetadataService();

        var completionEngine = new CompletionEngine(parser);
        // Spec 021 T101: DatabaseProvider stays in AkmlSql.Engine because of its
        // SqlClient dependency. Register it on the engine instance here so the named-pipe
        // path still provides USE-keyword database-list completion.
        completionEngine.RegisterProvider(new Completion.Providers.DatabaseProvider());
        var wildcardHandler = new WildcardExpansionHandler(parser);
        var signatureProvider = new SignatureProvider();
        var quickInfoProvider = new QuickInfoProvider();
        var formatHandler = new FormatRequestHandler(ProfileManager.CreateDefault());

        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AKML SQL");
        var personalSnippets = Path.Combine(appDataFolder, "snippets", "personal");
        var builtInSnippets = Path.Combine(AppContext.BaseDirectory, "snippets");
        var teamSnippets = ctx.EnsureSettings().Snippets.TeamFolder;
        var snippetHandler = new SnippetRequestHandler(personalSnippets, builtInSnippets,
            teamFolder: string.IsNullOrEmpty(teamSnippets) ? null : teamSnippets);

        var caSettingsLoader = new CaSettingsLoader();
        var ruleRegistry = new RuleRegistry();
        var analysisEngine = new AnalysisEngine(parser, ruleRegistry, caSettingsLoader);
        var refactoringEngine = new RefactoringEngine(parser, schemaCache);

        var safetyHandler = new SafetyCheckHandler(parser);
        var productivityHandler = new ProductivityRequestHandler(parser);
        var navigationHandler = new NavigationRequestHandler(schemaCache);
        var crudHandler = new CrudGenerationHandler(schemaCache);
        var scriptAsHandler = new ScriptAsHandler(schemaCache);
        var aiProviderTestHandler = new AiProviderTestHandler();

        // Spec 022 (M0 closure) -- P3 / US3 (complete). AiPipelineServices is the sole shared
        // collaborator surface for the seven AiHandlerBase-derived subclasses. It reads settings
        // fresh per call via ctx.EnsureSettings().Ai so the AnalysisSettingsChanged invalidation
        // propagates without an explicit AI refresh hook (FR-013). AiRequestHandler is deleted.
        var aiServices = AiPipelineServices.Build(schemaCache, parser, () => ctx.EnsureSettings().Ai);
        var sessionRequestHandler = new SessionRequestHandler();
        var gridExportService = new GridExportService();

        // History setup (per advisor guidance: build before registering handlers; closures
        // capture historyHandler directly, no lazy field-dereference indirection).
        var historyDb = new HistoryDatabase();
        var historyHandler = new HistoryRequestHandler(historyDb);
        var settings = ctx.EnsureSettings();
        var historyRetention = new HistoryRetentionService(historyDb, settings.History);
        if (settings.History.Enabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await historyDb.InitializeAsync();
                    await historyRetention.StartAsync();
                    Log.Information("History database and retention service started");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to initialize history database");
                }
            });
        }

        // SessionTrackerBridge so completion providers (e.g. DatabaseProvider) can resolve
        // the active connection string for a given session without a SessionManager reference.
        Completion.Providers.SessionTrackerBridge.Configure(sessionId =>
        {
            var s = sessions.GetSession(sessionId);
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

        // LookupSession helper used by AI / Navigation / CRUD / ScriptAs handlers.
        // Moved here from NamedPipeTransport per advisor's guidance -- keeps the transport file
        // focused on its own concerns; the closures below capture it directly.
        Func<string, (string? ConnectionString, string? DatabaseName)> lookupSession = sessionId =>
        {
            var session = sessions.GetSession(sessionId);
            if (session == null || !session.IsConnected) return (null, null);
            return (session.ConnectionString, session.DatabaseName);
        };

        // === Spec 014 stubs (FindUnusedVariables / EncryptedObjectDecryption) ===
        // Spec 030 T058: FindInvalidObjects is now a real handler (sys.sql_expression_dependencies), replacing its stub.
        var findInvalidHandler = new Handlers.Refactoring.FindInvalidObjectsHandler();
        var findUnusedStub = new Server.Stubs.FindUnusedVariablesHandlerStub();
        var encryptedStub = new Server.Stubs.EncryptedObjectDecryptionHandlerStub();
        router.RegisterRaw(MessageTypes.FindInvalidObjects, (msg, ct) => findInvalidHandler.HandleAsync(msg, lookupSession, ct));
        router.RegisterRaw(MessageTypes.FindUnusedVariables, (msg, ct) => findUnusedStub.HandleAsync(msg, ct));
        router.RegisterRaw(MessageTypes.EncryptedObjectDecryption, (msg, ct) => encryptedStub.HandleAsync(msg, ct));

        // === Completion (4 typed) ===
        router.Register(new Handlers.Completion.CompletionHandler(
            completionEngine, () => ctx.EnsureSettings()));
        router.Register(new Handlers.Completion.WildcardExpansionHandler(wildcardHandler));
        router.Register(new Handlers.Completion.SignatureHelpHandler(parser, signatureProvider));
        router.Register(new Handlers.Completion.QuickInfoHandler(parser, quickInfoProvider));

        // === Formatting (10 typed) ===
        router.Register(new Handlers.Formatting.FormatDocumentHandler(formatHandler));
        router.Register(new Handlers.Formatting.FormatSelectionHandler(formatHandler));
        router.Register(new Handlers.Formatting.FormatPreviewHandler(formatHandler));
        router.Register(new Handlers.Formatting.FormatActionHandler(formatHandler));
        router.Register(new Handlers.Formatting.ProfileListHandler(formatHandler));
        router.Register(new Handlers.Formatting.ProfileSaveHandler(formatHandler));
        router.Register(new Handlers.Formatting.ProfileDeleteHandler(formatHandler));
        router.Register(new Handlers.Formatting.ProfileImportHandler(formatHandler));
        router.Register(new Handlers.Formatting.ProfileExportSqlPromptHandler(formatHandler));
        router.Register(new Handlers.Formatting.StyleEditorSchemaHandler(formatHandler));
        router.Register(new Handlers.Formatting.DuplicateProfileHandler(formatHandler));

        // === Bulk Formatting (2 typed) ===
        router.Register(new Handlers.Formatting.BulkFormatHandler(formatHandler));
        router.Register(new Handlers.Formatting.BulkFormatCancelHandler(formatHandler));

        // === Analysis (3 typed) ===
        router.Register(new Handlers.Analysis.AnalysisHandler(
            analysisEngine, () => ctx.EnsureSettings()));
        // Spec 030 T052: Manage Rules dialog catalog. Shares ruleRegistry + caSettingsLoader with
        // AnalysisHandler so the reported enabled/severity match what analysis actually applies.
        router.Register(new Handlers.Analysis.ListAnalysisRulesHandler(
            ruleRegistry, caSettingsLoader, () => ctx.EnsureSettings()));
        router.Register(new Handlers.Analysis.AnalysisSettingsChangedHandler(() =>
        {
            caSettingsLoader.InvalidateCache();
            // Spec 022 (M0 closure) -- P3 / US3. ctx.InvalidateSettings() is the only refresh
            // hook now; the seven typed AI handlers read settings fresh on every call via
            // Services.SettingsProvider() -> ctx.EnsureSettings().Ai, so the explicit
            // aiHandler.RefreshSettings() call (and the AiRequestHandler class itself) is gone.
            ctx.InvalidateSettings();
            // PR-247 fix: flush stale batch-level cache so the next analysis re-runs rules
            // under the new settings rather than returning diagnostics computed under the old ones.
            analysisEngine.ClearBatchCache();
        }));

        // === Snippets (5 typed) ===
        router.Register(new Handlers.Snippets.SnippetExpandHandler(snippetHandler));
        router.Register(new Handlers.Snippets.SnippetListHandler(snippetHandler));
        router.Register(new Handlers.Snippets.SnippetSaveHandler(snippetHandler));
        router.Register(new Handlers.Snippets.SnippetDeleteHandler(snippetHandler));
        router.Register(new Handlers.Snippets.SnippetImportHandler(snippetHandler));

        // === Refactoring (2 typed, SwallowCancellation = true) ===
        router.Register(new Handlers.Refactoring.RefactorPreviewHandler(refactoringEngine));
        router.Register(new Handlers.Refactoring.RefactorApplyHandler(refactoringEngine));

        // === Schema (6 typed) ===
        var schemaRefreshService = new SchemaRefreshService(sessions, schemaCache, schemaMeta);
        router.Register(new Handlers.Schema.SchemaRefreshHandler(schemaRefreshService.Refresh));
        router.Register(new Handlers.Schema.SchemaStatusHandler());
        router.Register(new Handlers.Schema.SchemaIdentifyHandler(
            databaseLookup: sid => sessions.GetSession(sid)?.DatabaseName,
            identityResolver: sid =>
            {
                var session = sessions.GetSession(sid);
                if (session == null || !session.IsConnected) return null;
                return Handlers.Schema.SchemaIdentifyHandlerSupport
                    .ParseServerFromConnectionString(session.ConnectionString);
            }));
        router.Register(new Handlers.Schema.SchemaChecksumHandler(
            checksumFetcher: sid =>
            {
                var session = sessions.GetSession(sid);
                if (session == null || !session.IsConnected) return null;
                var cache = schemaCache.GetCache(sid, session.DatabaseName);
                if (cache == null) return null;
                int objectCount = 0;
                foreach (var schema in cache.Schemas.Values)
                {
                    objectCount += schema.Objects.Count;
                }
                return $"{cache.Phase}:{objectCount}";
            }));
        router.Register(new Handlers.Schema.SchemaPhaseAHandler(
            phaseLookup: (sid, db) =>
            {
                var session = sessions.GetSession(sid);
                if (session == null || !session.IsConnected) return (null, null);
                var cache = schemaCache.GetCache(sid, db);
                if (cache == null || cache.Phase == PopulationPhase.NotLoaded)
                    return (null, null);
                var bytes = Handlers.Schema.SchemaPhaseSerializer.SerializePhaseA(cache, db);
                var checksum = Handlers.Schema.SchemaPhaseSerializer.ComputeChecksum(cache);
                return (bytes, checksum);
            }));
        router.Register(new Handlers.Schema.SchemaPhaseBHandler(
            phaseLookup: (sid, db) =>
            {
                var session = sessions.GetSession(sid);
                if (session == null || !session.IsConnected) return (null, null);
                var cache = schemaCache.GetCache(sid, db);
                if (cache == null ||
                    cache.Phase == PopulationPhase.NotLoaded ||
                    cache.Phase == PopulationPhase.PhaseA)
                    return (null, null);
                var bytes = Handlers.Schema.SchemaPhaseSerializer.SerializePhaseB(cache, db);
                var checksum = Handlers.Schema.SchemaPhaseSerializer.ComputeChecksum(cache);
                return (bytes, checksum);
            }));

        // === Diagnostics (1 typed) ===
        router.Register(new Handlers.Diagnostics.EngineLogTailHandler());

        // === Control / lifecycle (5 typed) ===
        router.Register(new Handlers.Control.DocumentChangedHandler());
        router.Register(new Handlers.Control.PingHandler());
        router.Register(new Handlers.Control.ShutdownHandler());
        router.Register(new Handlers.Control.ConnectionChangedHandler());
        // Spec 029: validate SQL-auth credential before the shell stores it.
        router.Register(new Handlers.Control.TestSqlConnectionHandler());

        // === WebSocket bridge handshake (spec 025 US5 wire-up; spec 026 M4 closure auth) ===
        // The HandshakeHandler exists from spec 021 T060. The named-pipe transport doesn't need
        // it, but the WebSocket transport requires it as the first frame on every connection.
        // Spec 026 FR-013a/FR-013b: in LAN mode the host supplies a HandshakeHandler wired
        // (full ctor) to a live PairingService + BearerTokenStore so the PIN is enforced. When
        // null (loopback / named-pipe / no bridge), fall back to the parameterless auto-accept
        // handler — the spec-021 NO_AUTH localhost semantics stay intact.
        router.Register(handshakeHandler ?? new Handlers.Handshake.HandshakeHandler());

        // === AI handlers (7 typed via AiHandlerBase subclasses + 1 raw bridge for AiProviderTest) ===
        // Spec 022 P3/US3 complete: all seven user-facing AI messages route through typed
        // AiHandlerBase subclasses. AiProviderTest stays raw -- it's the developer-tool
        // provider-health-check handler, not a user-facing AI message.
        router.Register(new Handlers.Ai.AiTextToSqlHandler(aiServices));
        router.Register(new Handlers.Ai.AiExplainHandler(aiServices));
        router.Register(new Handlers.Ai.AiFixHandler(aiServices));
        router.Register(new Handlers.Ai.AiOptimizeHandler(aiServices));
        router.Register(new Handlers.Ai.AiIndexAnalysisHandler(aiServices));
        router.Register(new Handlers.Ai.AiChatHandler(aiServices));
        router.Register(new Handlers.Ai.AiGhostTextHandler(aiServices));
        router.RegisterRaw(MessageTypes.AiProviderTest, (msg, ct) => aiProviderTestHandler.HandleAsync(msg, ct));

        // AiStreamCancel is a notification-only signal -- ack and drop. The streaming pipeline
        // cancels via per-request CancellationToken at the shell side. When the engine's AI
        // pipeline exposes a streaming-cancel hook this dispatch ties into it.
        router.RegisterRaw(MessageTypes.AiStreamCancel, (msg, ct) =>
        {
            Log.Debug("AiStreamCancel received (requestId={Id})", msg.RequestId);
            return Task.FromResult<RpcMessage?>(null);
        });

        // === Session-recovery, History, Productivity, Navigation, CRUD/ScriptAs, GridExport (15 raw) ===
        router.RegisterRaw(MessageTypes.SessionSave,
            (msg, ct) => sessionRequestHandler.HandleAsync(msg, MessageTypes.SessionSave));
        router.RegisterRaw(MessageTypes.SessionRestore,
            (msg, ct) => sessionRequestHandler.HandleAsync(msg, MessageTypes.SessionRestore));
        router.RegisterRaw(MessageTypes.SessionDelete,
            (msg, ct) => sessionRequestHandler.HandleAsync(msg, MessageTypes.SessionDelete));

        router.RegisterRaw(MessageTypes.SafetyCheck, (msg, ct) => safetyHandler.HandleAsync(msg));

        router.RegisterRaw(MessageTypes.HistoryRecord, (msg, ct) => historyHandler.HandleRecordAsync(msg));
        router.RegisterRaw(MessageTypes.HistorySearch, (msg, ct) => historyHandler.HandleSearchAsync(msg));
        router.RegisterRaw(MessageTypes.HistoryAction, (msg, ct) => historyHandler.HandleActionAsync(msg));

        router.RegisterRaw(MessageTypes.StatementBoundary, (msg, ct) => productivityHandler.HandleStatementBoundaryAsync(msg));
        router.RegisterRaw(MessageTypes.DocumentOutline, (msg, ct) => productivityHandler.HandleDocumentOutlineAsync(msg));

        router.RegisterRaw(MessageTypes.GetObjectDefinition,
            (msg, ct) => navigationHandler.HandleGetObjectDefinitionAsync(msg, lookupSession, ct));
        router.RegisterRaw(MessageTypes.FindReferences,
            (msg, ct) => navigationHandler.HandleFindReferencesAsync(msg, lookupSession, ct));
        // Spec 030 T085: typed ObjectSearch handler (cache keyed by sessionId — fixes the legacy raw path's miss).
        router.Register(new Handlers.Productivity.ObjectSearchHandler());

        router.RegisterRaw(MessageTypes.CrudGeneration, (msg, ct) => crudHandler.HandleAsync(msg, lookupSession, ct));
        router.RegisterRaw(MessageTypes.ScriptAs, (msg, ct) => scriptAsHandler.HandleAsync(msg, lookupSession, ct));

        router.RegisterRaw(MessageTypes.GridExport, (msg, ct) => gridExportService.HandleAsync(msg));

        // === Spec 030 Phase 5: query execution + inline CRUD (3 raw) ===
        // Share the per-session persistent-connection registry (built in EngineComposition.Build) so
        // #temp/SET/USE state persists across executes on the SAME SqlConnection. ExecuteQuery and
        // ApplyChanges also take the schema cache for the is_primary_key cross-check. ExecuteCancel is
        // a notification (ack-and-drop), mirroring AiStreamCancel=78.
        var sessionConnections = ctx.SessionConnections
            ?? new Execution.SessionConnectionRegistry();
        var executeQueryHandler = new Execution.ExecuteQueryHandler(sessionConnections, schemaCache);
        var applyChangesHandler = new Execution.ApplyChangesHandler(sessionConnections);
        var executeCancelHandler = new Execution.ExecuteCancelHandler(sessionConnections);
        router.RegisterRaw(MessageTypes.ExecuteQuery,
            (msg, ct) => executeQueryHandler.HandleAsync(msg, lookupSession, ct));
        router.RegisterRaw(MessageTypes.ApplyChanges,
            (msg, ct) => applyChangesHandler.HandleAsync(msg, lookupSession, ct));
        router.RegisterRaw(MessageTypes.ExecuteCancel, (msg, ct) =>
        {
            executeCancelHandler.Handle(msg);
            return Task.FromResult<RpcMessage?>(null);
        });

        return historyRetention;
    }
}
