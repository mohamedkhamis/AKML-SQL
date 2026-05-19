using AkmlSql.Core.Config;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;

namespace AkmlSql.Engine
{
    /// <summary>
    /// Spec 021 (web edition) -- M0 transport abstraction.
    /// Spec 022 (M0 closure) -- P1: sole owner of the cached <see cref="AppSettings"/> after this
    /// closure. Per-process shared state passed to every <see cref="Transports.IRpcRequestHandler{TReq,TResp}"/>
    /// invocation by <see cref="RpcRouter"/>. Replaces the per-transport <c>_cachedSettings</c> field
    /// that previously lived on <c>PipeRpcServer</c> so the same handlers serve every transport with
    /// one cache surface.
    ///
    /// Access pattern: callers go through <see cref="EnsureSettings"/> (idempotent lazy load) and
    /// <see cref="InvalidateSettings"/> (drops the cache; next read re-invokes the loader). The
    /// loader itself is supplied at construction via <see cref="SettingsLoader"/> -- the composition
    /// root wires this to <see cref="ConfigManager.Load"/>; tests supply a stub.
    /// </summary>
    public sealed class RpcContext
    {
        private AppSettings? _cachedSettings;
        private readonly object _settingsLock = new();

        public required SessionManager Sessions { get; init; }
        public required SchemaCacheManager SchemaCache { get; init; }
        public required ILogger Logger { get; init; }

        /// <summary>
        /// On-disk settings loader. Wired by <c>EngineComposition</c> to <see cref="ConfigManager.Load"/>;
        /// tests supply a stub. Must be non-null at construction (the <c>required init</c> modifier
        /// enforces this on a C# 11+ compiler).
        /// </summary>
        public required Func<AppSettings> SettingsLoader { get; init; }

        /// <summary>Optional: needed by ConnectionChanged handler to set the parser's server version.</summary>
        public TsqlParserService? ParserService { get; init; }

        /// <summary>Optional: needed by ConnectionChanged handler to drive Phase A/B background population.</summary>
        public SchemaMetadataService? SchemaMetadata { get; init; }

        /// <summary>
        /// Returns the cached <see cref="AppSettings"/>. Calls <see cref="SettingsLoader"/> exactly once
        /// on first invocation; subsequent calls return the same instance until
        /// <see cref="InvalidateSettings"/> is called. Thread-safe via the internal lock.
        /// </summary>
        public AppSettings EnsureSettings()
        {
            if (_cachedSettings != null) return _cachedSettings;
            lock (_settingsLock)
            {
                return _cachedSettings ??= SettingsLoader();
            }
        }

        /// <summary>
        /// Drops the cached reference. The next <see cref="EnsureSettings"/> call re-invokes the loader.
        /// Thread-safe; may be called concurrently with <see cref="EnsureSettings"/>.
        /// </summary>
        public void InvalidateSettings()
        {
            lock (_settingsLock) { _cachedSettings = null; }
        }
    }
}
