using AkmlSql.Core.Config;
using AkmlSql.Engine.History;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;

namespace AkmlSql.Engine;

/// <summary>
/// Spec 022 (M0 closure) -- P2 / US2. Single composition root for the engine. Every transport
/// (named-pipe, in-process, WebSocket) consumes a <see cref="Build"/> result instead of
/// constructing its own services or wiring its own handler dictionary.
///
/// <para><c>Build</c> is idempotent within a process: call once per <see cref="EngineHost"/>
/// startup. Tests that need to inspect the registered router or share the per-process context
/// (e.g. <c>AllMessageTypesInProcessTests</c>) build their own composition and read the
/// <see cref="Router"/> / <see cref="Context"/> properties.</para>
/// </summary>
public sealed class EngineComposition
{
    public required RpcContext Context { get; init; }
    public required RpcRouter Router { get; init; }
    public required HistoryRetentionService HistoryRetention { get; init; }

    /// <summary>
    /// Build every engine service, the shared <see cref="RpcContext"/>, and an <see cref="RpcRouter"/>
    /// with every <see cref="Core.Ipc.MessageTypes"/> registered. <see cref="EngineHandlerRegistry"/>
    /// also starts the history-retention background loop (when history is enabled); the built
    /// <see cref="HistoryRetentionService"/> is exposed on <see cref="HistoryRetention"/> as a handle.
    /// </summary>
    public static EngineComposition Build(Handlers.Handshake.HandshakeHandler? handshakeHandler = null)
    {
        var sessions = new SessionManager();
        var ctx = new RpcContext
        {
            Sessions = sessions,
            // Spec 042: the cache key carries the session's kind, so one session's SQL Server and PostgreSQL databases of the
            // same name are two caches.
            SchemaCache = new SchemaCacheManager { KindOf = sessions.KindOf },
            Logger = Log.Logger,
            SettingsLoader = ConfigManager.Load,
            ParserService = new TsqlParserService(),
            SchemaMetadata = new SchemaMetadataService(),
            // Spec 030 — Phase 5: one registry shared by every transport so #temp/SET/USE state
            // persists across executes on the SAME per-session SqlConnection.
            SessionConnections = new Execution.SessionConnectionRegistry(),
        };

        // Spec 042 (FR-093): PostgreSQL's catalog reader exists only when the feature is switched on at start-up. The same loaded
        // settings decide this and every handler registration below; the handlers that gate PostgreSQL requests look at this
        // reader, not at settings re-read later, so a switch flipped while the engine runs waits for a restart, as the registrations do.
        ctx.PostgresSchemaMetadata = ctx.EnsureSettings().PostgreSql.Enabled ? new PostgreSql.PgSchemaMetadataService() : null;

        ctx.SchemaCache.ReloadMissing = (sessionId, database) => ReloadSessionCacheAsync(ctx, sessionId, database);

        var router = new RpcRouter
        {
            // Spec 042 (FR-092): surfaces with no PostgreSQL implementation answer a PostgreSQL session
            // themselves, before any handler runs. Inert for SQL Server sessions.
            DialectGuard = PostgreSql.DialectGuard.Create(),
        };
        var retention = EngineHandlerRegistry.RegisterAllHandlers(router, ctx, handshakeHandler);

        return new EngineComposition
        {
            Context = ctx,
            Router = router,
            HistoryRetention = retention,
        };
    }

    /// <summary>
    /// Loads an open session's own database cache again after <see cref="SchemaCacheManager.EvictLru"/>
    /// dropped it — the same Phase A then Phase B a connection change runs. Nothing for a session
    /// that is gone, or for another database than the session's.
    /// </summary>
    internal static async Task ReloadSessionCacheAsync(RpcContext ctx, string sessionId, string database)
    {
        var session = ctx.Sessions.GetSession(sessionId);
        var metadata = session == null ? null : ctx.SchemaMetadataFor(session.Kind);
        if (session == null || metadata == null || string.IsNullOrEmpty(session.ConnectionString)
            || !string.Equals(session.DatabaseName, database, StringComparison.OrdinalIgnoreCase))
            return;

        var cache = ctx.SchemaCache.GetOrCreateCache(sessionId, database);

        // Spec 043 follow-up (review, round 4): the population claim is the cache object's, so the reload claims the cache it is
        // about to load like every other load (a connect may have created it and be loading it already), and looks at the phase
        // only once it holds the claim: a load that ended a moment ago has left it loaded.
        if (!ctx.SchemaCache.TryClaimPopulation(cache)) return;
        try
        {
            if (cache.Phase != PopulationPhase.NotLoaded || cache.PermissionDenied) return;

            Log.Information("Reloading the evicted schema cache for {Session}:{Db}", sessionId, database);
            await metadata.PopulatePhaseAAsync(cache, session.ConnectionString, CancellationToken.None);
            ctx.SchemaCache.EvictLru();
            if (cache.Phase == PopulationPhase.PhaseA && !cache.PermissionDenied)
                await metadata.PopulatePhaseBAsync(cache, session.ConnectionString, CancellationToken.None);
        }
        finally
        {
            ctx.SchemaCache.ReleasePopulation(cache);
        }
    }
}
