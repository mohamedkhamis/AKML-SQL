#nullable enable
using System;
using System.Globalization;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.History;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Pure row-display helpers for the SQL History tool window list: the session name (or a raw-SQL
    /// fallback for the rare sessionless row) and the "&#215;N &#183; M versions" meta line.
    /// </summary>
    /// <remarks>
    /// Finding 10 (PR #249 review): both helpers below are now thin delegations to the shared
    /// <see cref="AkmlSql.Core.Models.History.HistoryDisplayName"/> (<c>Of</c> / <c>MetaFor</c>) --
    /// the ACTUAL formatting logic lives there exactly once, not hand-duplicated between this file
    /// and the web edition's <c>AkmlSql.Web.Pages.History</c>. This wrapper still exists (rather
    /// than pointing every call site directly at Core) because this project is a shared .projitems
    /// file compiled directly into six different VS-SDK-specific net472 assemblies and cannot
    /// reference AkmlSql.Web's net10.0 Blazor project — Core is the one dependency both hosts share.
    /// </remarks>
    internal static class HistoryRowDisplay
    {
        /// <summary>
        /// The session name (query-NN, a saved file name, or the user's rename). The raw-SQL fallback
        /// only fires for a row that somehow has no session at all, and is formatted via the shared
        /// <see cref="HistoryDisplayName.Of"/> — whitespace collapsed, truncated to ~60 characters —
        /// so a sessionless row never dumps raw multi-line SQL into the list.
        /// </summary>
        public static string DisplayNameFor(HistoryEntryDto e) =>
            HistoryDisplayName.Of(e.TabTitle, e.SqlText);

        /// <summary>"&#215;276 &#183; 12 versions". Both halves are omitted when they carry no information.</summary>
        public static string MetaFor(int executionCount, int versionCount) =>
            HistoryDisplayName.MetaFor(executionCount, versionCount);

        /// <summary>Spec 040 (HIS-13): the meta line, led by "Not executed" for a query that was never run (a draft).</summary>
        public static string MetaFor(int executionCount, int versionCount, int status)
        {
            var meta = MetaFor(executionCount, versionCount);
            if (status != (int)ExecutionStatus.NotExecuted) return meta;
            return meta.Length == 0 ? "Not executed" : "Not executed \u00B7 " + meta;
        }

        /// <summary>Spec 040 (HIS-05): "server · database" on the row's second line; either alone when the other is missing.</summary>
        public static string ConnectionLabel(string? server, string? database)
        {
            var hasServer = !string.IsNullOrEmpty(server);
            var hasDatabase = !string.IsNullOrEmpty(database);
            if (hasServer && hasDatabase) return server + " \u00B7 " + database;
            return hasServer ? server! : hasDatabase ? database! : string.Empty;
        }

        /// <summary>Spec 040 (HIS-05): the date group a row's ISO time falls in — "Today", "Yesterday", … "Older".</summary>
        public static string DateGroupFor(string? executedAt, DateTime now)
        {
            if (!DateTime.TryParse(executedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
                return HistoryDateGroups.Label(HistoryDateGroup.Older);
            return HistoryDateGroups.Label(HistoryDateGroups.For(now, when));
        }
    }
}
