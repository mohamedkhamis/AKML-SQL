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
    }
}
