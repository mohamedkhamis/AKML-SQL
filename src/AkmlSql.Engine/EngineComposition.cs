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
        var ctx = new RpcContext
        {
            Sessions = new SessionManager(),
            SchemaCache = new SchemaCacheManager(),
            Logger = Log.Logger,
            SettingsLoader = ConfigManager.Load,
            ParserService = new TsqlParserService(),
            SchemaMetadata = new SchemaMetadataService(),
            // Spec 030 — Phase 5: one registry shared by every transport so #temp/SET/USE state
            // persists across executes on the SAME per-session SqlConnection.
            SessionConnections = new Execution.SessionConnectionRegistry(),
        };

        ctx.SchemaCache.ReloadMissing = (sessionId, database) => ReloadSessionCacheAsync(ctx, sessionId, database);

        var router = new RpcRouter();
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
        if (session == null || ctx.SchemaMetadata == null || string.IsNullOrEmpty(session.ConnectionString)
            || !string.Equals(session.DatabaseName, database, StringComparison.OrdinalIgnoreCase))
            return;

        var cache = ctx.SchemaCache.GetOrCreateCache(sessionId, database);
        if (cache.Phase != PopulationPhase.NotLoaded || cache.PermissionDenied) return;

        Log.Information("Reloading the evicted schema cache for {Session}:{Db}", sessionId, database);
        await ctx.SchemaMetadata.PopulatePhaseAAsync(cache, session.ConnectionString, CancellationToken.None);
        ctx.SchemaCache.EvictLru();
        if (cache.Phase == PopulationPhase.PhaseA && !cache.PermissionDenied)
            await ctx.SchemaMetadata.PopulatePhaseBAsync(cache, session.ConnectionString, CancellationToken.None);
    }
}
