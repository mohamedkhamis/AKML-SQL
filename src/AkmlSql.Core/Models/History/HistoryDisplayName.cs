using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AkmlSql.Core.Models.History
{
    /// <summary>
    /// Pure display-name derivation shared by the web History page and the desktop History tool
    /// window. Returns the entry's custom tab title when present, otherwise a single-line preview of
    /// the SQL (whitespace runs collapsed, truncated to 60 characters with an ellipsis when longer),
    /// otherwise a placeholder for an empty entry.
    /// </summary>
    public static class HistoryDisplayName
    {
        private const int MaxLength = 60;
        private const string Placeholder = "(Untitled query)";

        /// <summary>
        /// Display name for a history entry: <paramref name="tabTitle"/> when it is non-whitespace,
        /// else <paramref name="sql"/> with whitespace collapsed and truncated to 60 characters
        /// (suffixed with "…" when truncated), else <c>"(Untitled query)"</c>.
        /// </summary>
        public static string Of(string? tabTitle, string? sql)
        {
            if (!string.IsNullOrWhiteSpace(tabTitle)) return tabTitle!;

            var collapsed = Regex.Replace(sql ?? string.Empty, @"\s+", " ").Trim();
            if (collapsed.Length == 0) return Placeholder;
            if (collapsed.Length <= MaxLength) return collapsed;
            // Avoid splitting a UTF-16 surrogate pair: if the last kept char is a high surrogate, its
            // low-surrogate partner is at index MaxLength, so cut one char earlier to drop the pair whole.
            var cut = char.IsHighSurrogate(collapsed[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
            return collapsed.Substring(0, cut) + "…";
        }

        /// <summary>
        /// Finding 10 (PR #249 review): the "×N runs · M versions" meta line, moved here from two
        /// hand-duplicated copies (<c>AkmlSql.Shell.Shared.History.HistoryRowDisplay.MetaFor</c> and
        /// <c>AkmlSql.Web.Pages.History.MetaFor</c>) -- both hosts already reference this assembly
        /// for <see cref="Of"/>, so there was no reason for the actual formatting logic to live
        /// twice. Both hosts now delegate their own <c>MetaFor</c> to this one. Each half is
        /// omitted when it carries no information: exactly one run and exactly one version
        /// produces an empty string (nothing to say).
        /// </summary>
        public static string MetaFor(int executionCount, int versionCount)
        {
            var parts = new List<string>(2);
            if (executionCount > 1) parts.Add($"×{executionCount}");
            if (versionCount > 1) parts.Add($"{versionCount} versions");
            return string.Join(" · ", parts);
        }
    }
}
