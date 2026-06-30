using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Core.Text
{
    /// <summary>
    /// Single shared implementation of search-box → highlight-term extraction for the SQL HISTORY
    /// previews on both editions (the web History page and the desktop History tool window). Was
    /// previously duplicated and divergent between
    /// <c>AkmlSql.Web/Services/HistoryPreviewHighlighter</c> and
    /// <c>AkmlSql.Shell.Shared/History/HistoryToolWindowControl</c>; this is the canonical, quote-aware
    /// behaviour (the web version) that both now delegate to. Pure C# only (netstandard2.0 + net10.0).
    /// </summary>
    public static class HistorySearchTerms
    {
        // Prefix filters whose value targets metadata (server/db/name/flags) rather than the SQL body —
        // their value never appears in the preview, so it must NOT become a highlight term. The "sql:"
        // prefix is the exception: its value is a free-text FTS query against the SQL body, so we keep it.
        private static readonly HashSet<string> NonTextPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "server", "db", "database", "name", "starred", "is", "open",
        };

        /// <summary>Extracts plain highlight terms from the search box text. Tokenizes respecting
        /// double-quoted spans (a quoted span is one term, quotes stripped); drops bare AND/OR/NOT boolean
        /// keywords (case-insensitive); drops the value of non-text prefix filters
        /// (server:/db:/database:/name:/starred:/is:/open:) entirely while keeping a text/SQL prefix's
        /// value (sql:) as a highlight term; unknown prefixes pass through as literal text; and strips a
        /// single trailing FTS5 <c>*</c>.</summary>
        public static IReadOnlyList<string> Extract(string? search)
        {
            if (string.IsNullOrWhiteSpace(search)) return Array.Empty<string>();
            var terms = new List<string>();
            foreach (var token in TokenizeSearch(search!))
            {
                // Bare boolean operators are checked on the raw token, so a quoted "AND" survives as a phrase.
                if (token.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("NOT", StringComparison.OrdinalIgnoreCase)) continue;

                var candidate = token;
                int colon = PrefixColonIndex(token);
                if (colon > 0)
                {
                    var prefix = token.Substring(0, colon);
                    if (NonTextPrefixes.Contains(prefix)) continue;            // server:/db:/name:/... — drop entirely
                    if (prefix.Equals("sql", StringComparison.OrdinalIgnoreCase))
                        candidate = token.Substring(colon + 1);                // sql:<value> — highlight the value
                    // else: unknown prefix → keep the whole token as literal text (matches the desktop parser)
                }

                var t = candidate.Replace("\"", string.Empty);                 // a quoted span is one term, quotes stripped
                if (t.EndsWith("*", StringComparison.Ordinal)) t = t.Substring(0, t.Length - 1); // FTS5 prefix wildcard
                t = t.Trim();
                if (t.Length > 0 && !terms.Contains(t, StringComparer.OrdinalIgnoreCase)) terms.Add(t);
            }
            return terms;
        }

        /// <summary>Splits search text into tokens, treating a double-quoted span (including any spaces
        /// inside) as a single token with its quote characters retained for downstream stripping.</summary>
        private static IEnumerable<string> TokenizeSearch(string query)
        {
            var tokens = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            foreach (char c in query)
            {
                if (c == '"') { inQuotes = !inQuotes; sb.Append(c); continue; }
                if (!inQuotes && (c == ' ' || c == '\t' || c == '\r' || c == '\n'))
                {
                    if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0) tokens.Add(sb.ToString());
            return tokens;
        }

        /// <summary>Index of a <c>prefix:</c> colon (non-empty prefix, occurring before any quote), or -1
        /// when the token is not a prefix filter (e.g. a quoted phrase or a colon-free word).</summary>
        private static int PrefixColonIndex(string token)
        {
            int quote = token.IndexOf('"');
            int colon = token.IndexOf(':');
            if (colon <= 0) return -1;                 // no colon, or leading colon (no prefix)
            if (quote >= 0 && quote < colon) return -1; // colon is inside a quoted span — not a prefix
            return colon;
        }
    }
}
