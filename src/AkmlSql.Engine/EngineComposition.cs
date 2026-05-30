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
        };

        var router = new RpcRouter();
        var retention = EngineHandlerRegistry.RegisterAllHandlers(router, ctx, handshakeHandler);

        return new EngineComposition
        {
            Context = ctx,
            Router = router,
            HistoryRetention = retention,
        };
    }
}
