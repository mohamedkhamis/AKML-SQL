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

        /// <summary>
        /// SQL Prompt styles: the engine lists, reads (<c>ProfileGetResponse.SqlPromptJson</c>),
        /// saves and previews styles written in SQL Prompt's model, stored in the same styles
        /// folder SSMS uses. The web edition saves its styles through the engine
        /// when this is advertised.
        /// </summary>
        public const string StylesSqlPromptV1 = "styles.sqlprompt.v1";

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
        /// Spec 041 (contracts/ipc.md) — the execute reply carries structured messages, batches, the
        /// completion time and the omitted-message count (keys 8–11), sets carry their batch, and
        /// <c>ExecuteParse</c> (219 → 220) is understood. Scripts are split on GO and keep running after a
        /// failing batch.
        /// </summary>
        public const string ExecuteV2 = "execute.v2";

        /// <summary>
        /// Spec 041 (R25–R27, contracts/ipc.md): the database selector — <c>ListDatabasesRequest.SessionId</c> (the
        /// list from the connection the engine holds), <c>ChangeDatabase</c> (217 → 218) and
        /// <c>ExecuteQueryResult.CurrentDatabase</c> (the database a script's <c>USE</c> left the session in).
        /// </summary>
        public const string SessionDatabaseV1 = "session.database.v1";

        /// <summary>
        /// <c>SessionDisconnect</c> (225 → 226): the web edition's Disconnect rolls back the session's open transaction and
        /// closes its connection before it reports success.
        /// </summary>
        public const string SessionDisconnectV1 = "session.disconnect.v1";

        /// <summary>
        /// Spec 043 follow-up: the web edition may ask the engine to remember a SQL Server login's password
        /// (<c>TestSqlConnectionRequest.RememberPassword</c>), connect without sending it, and ask about or forget it through
        /// <c>CredentialRequest</c> (221 → 222) with the SQL Server kind (<c>SqlServerCredentialResolver</c>). The credential pair is
        /// registered whatever <c>postgreSql.enabled</c> says, since this is always advertised (review; the engines that advertise only this id
        /// registered it only with the feature on, see <see cref="CredentialsSqlServerV2"/>). Still advertised next to
        /// <see cref="CredentialsSqlServerV2"/>: older web clients look for this id.
        /// </summary>
        public const string CredentialsSqlServerV1 = "credentials.sqlserver.v1";

        /// <summary>
        /// Spec 043 follow-up (review): what the released engines that advertise only <see cref="CredentialsSqlServerV1"/>
        /// (1.26.1022.0927, 1.26.1022.1042) do not do. <c>TestSqlConnectionResponse.Remembered</c> (key 4) is sent — true only when
        /// the password is now in the engine's store — and the remembered password is bound to a plain SQL login
        /// (<c>BridgeSqlSecurity.IsPlainSqlLogin</c>) and to the certificate checking it was given with. A client must not read a
        /// missing key 4 from an engine without this id as "not remembered". <c>CredentialRequest</c> is answered whatever
        /// <c>postgreSql.enabled</c> says (the v1-only engines registered it only with the feature on, so a SQL Server request to
        /// one with the feature off gets no reply), and a SQL Server Has honours key 5 <c>TrustServerCertificate</c> (review,
        /// round 3: yes only for a password a connection with that choice would be given).
        /// </summary>
        public const string CredentialsSqlServerV2 = "credentials.sqlserver.v2";

        /// <summary>
        /// Spec 043 follow-up: <c>ConnectionInfo.CopyPasswordFromSessionId</c> (key 7) and
        /// <c>TestSqlConnectionRequest.CopyPasswordFromSessionId</c> (key 2) — a new query tab opened on a connected tab takes that
        /// session's password for the same server, login and security options (<c>SessionPasswordCopy</c>), as SSMS's New Query does.
        /// Still advertised next to <see cref="SessionCopyV2"/>: older web clients look for this id.
        /// </summary>
        public const string SessionCopyV1 = "session.copy.v1";

        /// <summary>
        /// Spec 043 follow-up (review): what the released engines that advertise only <see cref="SessionCopyV1"/> do not do. The copy
        /// source is honoured on <c>ListDatabasesRequest.CopyPasswordFromSessionId</c> (key 3), and a copy's origin is the web page
        /// (<c>HandshakeRequest.ClientInstanceId</c>, key 6) rather than the socket, so it survives the page's reconnect after a link
        /// drop.
        /// </summary>
        public const string SessionCopyV2 = "session.copy.v2";

        /// <summary>
        /// Spec 042 (contracts/ipc.md): the engine can connect to, run scripts against, browse and
        /// provide language services for PostgreSQL servers. Advertised only when
        /// <c>config.json</c> <c>postgreSql.enabled</c> is true (the default), so an engine built
        /// before the feature, or one with it switched off, never claims it (FR-006, FR-093).
        /// </summary>
        public const string DialectPostgreSqlV1 = "dialect.postgresql.v1";

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
            DiagnosticsEngineLogTailV1,
            SnippetsWrite,
            StylesSqlPromptV1,
            ExecuteV2,
            SessionDatabaseV1,
            SessionDisconnectV1,
            CredentialsSqlServerV1,
            CredentialsSqlServerV2,
            SessionCopyV1,
            SessionCopyV2,
        };

        /// <summary>
        /// The capabilities to advertise for the given settings: <see cref="Current"/> plus
        /// <see cref="DialectPostgreSqlV1"/> when PostgreSQL support is enabled (spec 042). Null settings
        /// (a context built without a loader) mean the defaults, so PostgreSQL is on.
        /// </summary>
        public static string[] For(AkmlSql.Core.Config.AppSettings? settings)
        {
            var enabled = settings?.PostgreSql?.Enabled ?? true;
            if (!enabled) return Current.ToArray();

            var list = new List<string>(Current.Count + 1);
            list.AddRange(Current);
            list.Add(DialectPostgreSqlV1);
            return list.ToArray();
        }

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
