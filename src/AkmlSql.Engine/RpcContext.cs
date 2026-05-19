using AkmlSql.Core.Config;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;

namespace AkmlSql.Engine
{
    /// <summary>
    /// Spec 021 (web edition) — M0 transport abstraction.
    /// Per-process shared state passed to every <see cref="Transports.IRpcRequestHandler{TRequest,TResponse}"/>
    /// invocation by <see cref="RpcRouter"/>. Replaces the per-server fields that lived on
    /// <c>PipeRpcServer</c> (in particular <c>_cachedSettings</c>) so the same handler classes can be
    /// served by named-pipe, in-process, and WebSocket transports without each transport carrying its
    /// own copy of these dependencies.
    ///
    /// The <see cref="Settings"/> reference is replaced atomically when the
    /// <c>AnalysisSettingsChanged</c> message arrives — the field is intentionally not declared
    /// <see langword="readonly"/>.
    /// </summary>
    public sealed class RpcContext
    {
        public AppSettings? Settings { get; set; }
        public required SessionManager Sessions { get; init; }
        public required SchemaCacheManager SchemaCache { get; init; }
        public required ILogger Logger { get; init; }

        /// <summary>Optional: needed by ConnectionChanged handler to set the parser's server version.</summary>
        public TsqlParserService? ParserService { get; init; }

        /// <summary>Optional: needed by ConnectionChanged handler to drive Phase A/B background population.</summary>
        public SchemaMetadataService? SchemaMetadata { get; init; }
    }
}
