using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Server;
using AkmlSql.PostgreSql.Analysis;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Analysis
{
    // Spec 021 (web edition) -- M0.3 task T014. Two typed handlers for the analysis pipeline:
    //   * AnalysisHandler             -- RequestAnalyze
    //   * AnalysisSettingsChangedHandler -- AnalysisSettingsChanged (notification)
    //
    // AnalysisHandler opts in to SwallowCancellation=true so a cancelled analysis run does
    // NOT propagate OCE through the outer DispatchAsync (which would tear down the pipe loop).
    // This mirrors the prior inline AnalyzeAsync behaviour exactly.

    public sealed class AnalysisHandler : IRpcRequestHandler<CodeAnalysisRequest, CodeAnalysisResponse>
    {
        private readonly AnalysisEngine _engine;
        private readonly Func<AppSettings> _settingsProvider;
        private readonly PgAnalysisEngine? _pg;

        /// <param name="pg">
        /// Spec 042: the PostgreSQL analyser, present when <c>postgreSql.enabled</c> is on. A PostgreSQL session is analysed by it and
        /// never by <paramref name="engine"/>; with the dialect off the answer for such a session is "no findings".
        /// </param>
        public AnalysisHandler(AnalysisEngine engine, Func<AppSettings> settingsProvider, PgAnalysisEngine? pg = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            _pg = pg;
        }

        public int RequestMessageType => MessageTypes.RequestAnalyze;
        public int ResponseMessageType => MessageTypes.AnalysisResult;
        public bool SwallowCancellation => true;

        public Task<CodeAnalysisResponse> HandleAsync(CodeAnalysisRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            // Spec 042 (FR-090, FR-092, FR-152, SC-010): the rules, the parser and the suppressions behind this engine are T-SQL's. A
            // PostgreSQL session's text is never given to them - they would report PostgreSQL syntax as T-SQL syntax errors. It is
            // analysed by the PostgreSQL rules (the schema cache of the connection lets the schema-aware ones run); with the dialect
            // switched off the answer is "no findings", the version echoed so the client moves on.
            if (ctx.Sessions.KindOf(request.SessionId) == AkmlSql.Core.DatabaseKind.PostgreSql)
            {
                if (_pg == null)
                {
                    return Task.FromResult(new CodeAnalysisResponse
                    {
                        RequestId = request.RequestId,
                        Issues = Array.Empty<CodeIssueInfo>(),
                        AnalyzedVersion = request.DocumentVersion,
                    });
                }

                var pgSession = ctx.Sessions.GetSession(request.SessionId);
                var pgCache = pgSession != null ? ctx.SchemaCache.GetCache(pgSession.SessionId, pgSession.DatabaseName) : null;
                return Task.FromResult(_pg.Analyze(request, pgCache, _settingsProvider().CodeAnalysis, ct));
            }

            var settings = _settingsProvider();

            // Spec 021 F1: AnalysisEngine no longer takes SessionManager / SchemaCacheManager
            // (those live in AkmlSql.Engine and the extracted AkmlSql.Analysis library cannot
            // depend on them). Resolve the session here and pass the int serverVersion +
            // DatabaseCache? schemaCache pair directly.
            var session = ctx.Sessions.GetSession(request.SessionId);
            var schemaCache = session != null
                ? ctx.SchemaCache.GetOrCreateCache(session.SessionId, session.DatabaseName)
                : null;
            var serverVersion = session?.ServerVersion ?? 0;

            return _engine.AnalyzeAsync(request, serverVersion, schemaCache, settings.CodeAnalysis, ct);
        }
    }

    /// <summary>
    /// Notification (ResponseMessageType = 0). Invalidates caches the engine maintains for
    /// analysis settings. Wired via callbacks provided by <see cref="NamedPipeTransport"/>.
    /// </summary>
    public sealed class AnalysisSettingsChangedHandler
        : IRpcRequestHandler<AnalysisSettingsChangedRequest, AnalysisSettingsChangedRequest>
    {
        private readonly Action _onSettingsChanged;

        public AnalysisSettingsChangedHandler(Action onSettingsChanged)
        {
            _onSettingsChanged = onSettingsChanged ?? throw new ArgumentNullException(nameof(onSettingsChanged));
        }

        public int RequestMessageType => MessageTypes.AnalysisSettingsChanged;
        public int ResponseMessageType => 0;    // fire-and-forget
        public bool AllowsEmptyPayload => true;

        public Task<AnalysisSettingsChangedRequest> HandleAsync(
            AnalysisSettingsChangedRequest request, RpcContext ctx, CancellationToken ct)
        {
            _onSettingsChanged();
            return Task.FromResult(new AnalysisSettingsChangedRequest());
        }
    }
}
