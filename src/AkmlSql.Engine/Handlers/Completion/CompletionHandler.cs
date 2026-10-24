using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Completion
{
    /// <summary>
    /// Spec 021 (web edition) — M0.2 task T011. First migration of an inline switch case from
    /// <c>NamedPipeTransport</c> into the new <see cref="IRpcRequestHandler{TRequest,TResponse}"/>
    /// abstraction. Other handlers follow at T013 onwards.
    ///
    /// Behavioural parity is the contract here: this handler must produce a byte-identical
    /// <see cref="CompletionResponse"/> to what the prior inline case produced. The perf
    /// baseline test (<c>PerformanceBaselineTests</c>) enforces the &lt;= 5 % regression
    /// envelope; the existing <c>CompletionEngineTests</c> suite enforces functional parity
    /// by exercising the same engine instance directly.
    /// </summary>
    public sealed class CompletionHandler : IRpcRequestHandler<CompletionRequest, CompletionResponse>
    {
        private readonly CompletionEngine _engine;
        private readonly Func<AppSettings> _settingsProvider;
        private readonly PostgreSql.PgServices? _pg;

        /// <param name="engine">
        /// The shared <see cref="CompletionEngine"/>. Owned by the engine host, not the handler;
        /// stateful settings on the engine (TableAliasEnabled, JoinAssistEnabled, ...) are pushed
        /// per request from <paramref name="settingsProvider"/>.
        /// </param>
        /// <param name="settingsProvider">
        /// Returns the currently-active <see cref="AppSettings"/>. Called once per request so a
        /// caller-side invalidation (e.g. AnalysisSettingsChanged) takes effect on the next
        /// completion call without a handler re-construction.
        /// </param>
        public CompletionHandler(CompletionEngine engine, Func<AppSettings> settingsProvider, PostgreSql.PgServices? pg = null)
        {
            _pg = pg;
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        }

        public int RequestMessageType => MessageTypes.RequestCompletion;
        public int ResponseMessageType => MessageTypes.CompletionResult;

        public Task<CompletionResponse> HandleAsync(CompletionRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var session = ctx.Sessions.GetSession(request.SessionId);
            var documentText = session?.DocumentText ?? string.Empty;
            var dbCache = session != null
                ? ctx.SchemaCache.GetCache(request.SessionId, session.DatabaseName)
                : null;

            // Spec 042 (FR-061): a PostgreSQL session is completed by the PostgreSQL engine, never by the T-SQL one.
            if (session?.Kind == AkmlSql.Core.DatabaseKind.PostgreSql && _pg != null)
                return Task.FromResult(_pg.Complete(documentText, request.CursorOffset, dbCache, session, _settingsProvider()));

            // Mirror NamedPipeTransport behaviour: push IntelliSense settings onto the engine before
            // each request so toggles in the Settings dialog take effect immediately.
            // NOTE: the engine has mutable per-instance settings. Spec 043 answers a browser's requests side by side, so two
            // completions (two query tabs, or the pipe and a socket) could interleave these writes with each other's
            // GetCompletions: the settings writes and the completion run under one lock (per-call settings would lift it).
            lock (_engine)
            {
                return Task.FromResult(CompleteWithSettings(session, documentText, dbCache, request));
            }
        }

        private CompletionResponse CompleteWithSettings(AkmlSql.Engine.Server.SessionState? session, string documentText, AkmlSql.Engine.Schema.DatabaseCache? dbCache, CompletionRequest request)
        {
            var settings = _settingsProvider();
            _engine.TableAliasEnabled        = settings.IntelliSense.AutoAlias;
            _engine.JoinAssistEnabled        = settings.IntelliSense.JoinAssist;
            _engine.IncludeKeywords          = settings.IntelliSense.SuggestionTypes.IncludeKeywords;
            _engine.IncludeSystemObjects     = settings.IntelliSense.SuggestionTypes.IncludeSystemObjects;
            _engine.SchemaQualifyMode        = settings.IntelliSense.Qualification.SchemaMode;
            _engine.BracketMode              = settings.IntelliSense.Qualification.BracketMode;
            _engine.MatchByColumnName        = settings.IntelliSense.JoinOptions.MatchByColumnName;
            _engine.ColumnScopeMode          = settings.IntelliSense.SuggestionTypes.ColumnScope;
            _engine.AliasIncludeAs           = settings.IntelliSense.AliasOptions.IncludeAs;
            _engine.AliasObjectMap           = settings.IntelliSense.AliasOptions.ObjectAliasMap;
            _engine.AliasPrefixesToIgnore    = settings.IntelliSense.AliasOptions.PrefixesToIgnore;

            // Spec 040 (OPT-01): the Suggestions › Behavior settings that used to change nothing.
            _engine.SetMaxSuggestions(Math.Max(5, Math.Min(200, settings.IntelliSense.MaxSuggestions)));
            _engine.FuzzyMatchEnabled        = settings.IntelliSense.FuzzyMatch;
            _engine.ShowDataTypes            = settings.IntelliSense.ShowDataTypes;
            _engine.ShowNullability          = settings.IntelliSense.ShowNullability;
            _engine.ShowKeyIndicators        = settings.IntelliSense.ShowPkFk;

            // Spec 030 T036 / FR-016 — suggestion connection scope. The schema cache is single-database,
            // so the database allow-list resolves to a single in-scope bool against the session's database
            // (suppress when the connected DB is excluded); the schema allow-list filters the object list.
            var scope = settings.IntelliSense.ConnectionScope;
            _engine.ScopeSchemas         = scope.Schemas ?? Array.Empty<string>();
            _engine.DatabaseInScope      = scope.IncludesDatabase(session?.DatabaseName);
            _engine.IncludeLinkedServers = scope.IncludeLinkedServers;

            return _engine.GetCompletions(documentText, request.CursorOffset, dbCache, request.SessionId);
        }
    }
}
