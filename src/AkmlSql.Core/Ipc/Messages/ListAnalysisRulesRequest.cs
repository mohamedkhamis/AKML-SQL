using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 030 T052 — request the full analysis rule catalog (one entry per discovered
    /// <c>IAnalysisRule</c>) so the shell can populate the Manage Rules dialog.
    /// Sent Shell -> Engine as MessageType 33 (ListAnalysisRules). Pairs with response 133.
    /// </summary>
    [MessagePackObject]
    public class ListAnalysisRulesRequest
    {
        /// <summary>
        /// Optional absolute directory of the active document. When provided, per-project
        /// <c>.casettings</c> overrides (enabled/severity) are resolved upward from this directory
        /// so the reported Enabled/EffectiveSeverity match what analysis would actually apply.
        /// Empty = global defaults only.
        /// </summary>
        [Key(0)]
        public string FileDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Spec 042: also list the PostgreSQL rule set (each row's <c>Kind</c> says which set it belongs to). Off by default, so a client
        /// that predates the feature - the SSMS rules window - keeps receiving exactly the SQL Server rules.
        /// </summary>
        [Key(1)]
        public bool IncludePostgreSql { get; set; }
    }
}
