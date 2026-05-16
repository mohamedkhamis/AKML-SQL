using System;
using System.Collections.Generic;

namespace AkmlSql.Engine
{
    /// <summary>
    /// Spec 021 (web edition) -- M3 task T061. Stable capability identifiers advertised
    /// by the engine in <see cref="AkmlSql.Core.Ipc.Messages.HandshakeResponse.EngineCapabilities"/>.
    /// The web client tracks the engine's capability list and gates per-feature UI on it:
    /// missing capability -> inline "feature requires engine version >= X" notice, NOT a
    /// full-page blocker. See clarification 5 in spec.md and contracts/rpc-handshake.md.
    /// </summary>
    public static class Capabilities
    {
        /// <summary>Formatter pipeline available. Always present from M0+.</summary>
        public const string CoreFormatV1 = "core.format.v1";

        /// <summary>Analyser rules available. Always present from M0+.</summary>
        public const string CoreAnalysisV1 = "core.analysis.v1";

        /// <summary>Live schema and IntelliSense (M3).</summary>
        public const string SchemaV2 = "schema.v2";

        /// <summary>
        /// Schema-cache identity protocol (M5 / T104). Engine reports a stable
        /// <c>serverCanonicalIdentity</c> in <c>HandshakeResponse</c> and exposes a
        /// <c>SchemaIdentify</c> message.
        /// </summary>
        public const string SchemaCacheV1 = "schema.cache.v1";

        /// <summary>Snippet save/delete via the bridge (M5).</summary>
        public const string SnippetsWrite = "snippets.write";

        /// <summary>Heavyweight schema-aware refactorings (M5).</summary>
        public const string RefactoringHeavy = "refactoring.heavy";

        /// <summary>
        /// AI Text-to-SQL via the bridge. AI invocation in the web edition goes
        /// direct-to-provider (FR-030); this capability covers any engine-hosted helpers
        /// the M6 design adds (currently none).
        /// </summary>
        public const string AiTextToSqlV1 = "ai.text-to-sql.v1";

        /// <summary>
        /// The <c>EngineLogTail</c> request used by the diagnostics bundle (M2 ring buffer;
        /// engine extension lands at M2 alongside the diagnostics export).
        /// </summary>
        public const string DiagnosticsEngineLogTailV1 = "diagnostics.engine-log-tail.v1";

        /// <summary>
        /// The set of capabilities the current engine build advertises. M3 baseline includes
        /// the always-available capabilities; <see cref="SchemaCacheV1"/> joined the list when
        /// T104 (SchemaIdentify) shipped. Future tasks add to this list:
        /// <list type="bullet">
        ///   <item><see cref="SnippetsWrite"/> when T115 wires snippet save/delete.</item>
        ///   <item><see cref="RefactoringHeavy"/> when T117 wires heavy refactorings.</item>
        ///   <item><see cref="DiagnosticsEngineLogTailV1"/> when the engine log tail handler ships.</item>
        /// </list>
        /// </summary>
        public static readonly IReadOnlyList<string> Current = new[]
        {
            CoreFormatV1,
            CoreAnalysisV1,
            SchemaV2,
            SchemaCacheV1,
        };

        /// <summary>
        /// Lowest protocol version the current engine supports. <c>1</c> through M3.
        /// </summary>
        public const int ProtocolVersionMin = 1;

        /// <summary>
        /// Highest protocol version the current engine supports.
        /// </summary>
        public const int ProtocolVersionMax = 1;
    }
}
