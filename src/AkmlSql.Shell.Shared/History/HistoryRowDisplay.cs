#nullable enable
using System.Collections.Generic;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Pure row-display helpers for the SQL History tool window list: the session name (or a raw-SQL
    /// fallback for the rare sessionless row) and the "&#215;N &#183; M versions" meta line.
    /// </summary>
    /// <remarks>
    /// Deliberately duplicated from the web edition's <c>AkmlSql.Web.Pages.History.DisplayNameFor</c> /
    /// <c>MetaFor</c> rather than shared via a new assembly: this project is a shared .projitems file
    /// compiled directly into six different VS-SDK-specific net472 assemblies, and it cannot reference
    /// AkmlSql.Web's net10.0 Blazor project. Keep both copies in lockstep by hand.
    /// </remarks>
    internal static class HistoryRowDisplay
    {
        /// <summary>
        /// The session name (query-NN, a saved file name, or the user's rename). The raw-SQL fallback
        /// only fires for a row that somehow has no session at all.
        /// </summary>
        public static string DisplayNameFor(HistoryEntryDto e) =>
            !string.IsNullOrWhiteSpace(e.TabTitle)
                ? e.TabTitle!
                : (e.SqlText ?? string.Empty).Trim();

        /// <summary>"&#215;276 &#183; 12 versions". Both halves are omitted when they carry no information.</summary>
        public static string MetaFor(int executionCount, int versionCount)
        {
            var parts = new List<string>(2);
            if (executionCount > 1) parts.Add($"×{executionCount}");
            if (versionCount > 1) parts.Add($"{versionCount} versions");
            return string.Join(" · ", parts);
        }
    }
}
