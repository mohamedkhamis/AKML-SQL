#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Parses advanced search syntax for the SQL History search bar.
    /// Supports prefix filters (server:, database:, db:, sql:, name:, path:, starred:, open:,
    /// date:[yyyyMMdd TO yyyyMMdd]),
    /// quoted phrases, wildcard characters (* for FTS5 prefix queries), boolean keywords (OR, NOT),
    /// and CamelCase token detection (short all-uppercase 2-4 char tokens like PC, GCO, SCO).
    /// </summary>
    internal static class HistorySearchParser
    {
        // FTS5 boolean keywords that should be preserved as-is in the query string.
        private static readonly HashSet<string> Fts5BooleanKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "OR", "NOT", "AND"
        };

        /// <summary>
        /// Spec 040 (T132, HIS-07) — every search form, with an example, for the "?" popup beside the
        /// search box. <c>HistorySearchParserTests</c> parses each one.
        /// </summary>
        internal static readonly IReadOnlyList<(string Syntax, string Meaning)> HelpRows = new[]
        {
            ("orders customer", "Queries containing every word, in their SQL, name, file path, server or database"),
            ("\"order details\"", "An exact phrase"),
            ("cust*", "Words starting with \"cust\""),
            ("orders OR invoices", "Either word"),
            ("orders NOT archive", "The first word but not the second"),
            ("name:monthly", "Query (tab) name contains \"monthly\""),
            ("path:Reports", "File path contains \"Reports\""),
            ("sql:\"GROUP BY\"", "Only the SQL text"),
            ("server:prod-sql", "Ran on a server"),
            ("database:Northwind", "Ran in a database"),
            ("db:Northwind", "Same as database:"),
            ("starred:true", "Starred queries (starred:false for the rest)"),
            ("open:true", "Queries open in a tab now (open:false for closed ones)"),
            ("date:[20260901 TO 20260930]", "Ran between two dates; use * for an open end"),
        };

        public static ParsedSearch Parse(string query)
        {
            var result = new ParsedSearch();

            if (string.IsNullOrWhiteSpace(query))
            {
                result.PlainTextQuery = string.Empty;
                return result;
            }

            var tokens = Tokenize(query);
            var plainParts = new List<string>();
            var camelCaseTokens = new List<string>();

            foreach (var token in tokens)
            {
                // Check for prefix:value patterns
                var colonIdx = token.IndexOf(':');
                if (colonIdx > 0 && colonIdx < token.Length - 1 && !token.StartsWith("\"", StringComparison.Ordinal))
                {
                    var prefix = token.Substring(0, colonIdx).ToLowerInvariant();
                    var value = token.Substring(colonIdx + 1);

                    // Strip surrounding quotes from value if present
                    if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }

                    switch (prefix)
                    {
                        case "server":
                            result.ServerFilter = value;
                            result.HasPrefixes = true;
                            continue;

                        case "database":
                        case "db":
                            result.DatabaseFilter = value;
                            result.HasPrefixes = true;
                            continue;

                        case "sql":
                            result.SqlFilter = value;
                            result.HasPrefixes = true;
                            continue;

                        case "name":
                            result.NameFilter = value;
                            result.HasPrefixes = true;
                            continue;

                        case "path":
                            result.PathFilter = value;
                            result.HasPrefixes = true;
                            continue;

                        case "date":
                            if (TryParseDateRange(value, out var from, out var to))
                            {
                                result.DateFrom = from;
                                result.DateTo = to;
                                result.HasPrefixes = true;
                                continue;
                            }
                            break; // not a valid range: free text

                        case "starred":
                            if (bool.TryParse(value, out var starredVal))
                            {
                                result.StarredFilter = starredVal;
                                result.HasPrefixes = true;
                            }
                            continue;

                        case "open":
                            if (bool.TryParse(value, out var openVal))
                            {
                                result.OpenFilter = openVal;
                                result.HasPrefixes = true;
                            }
                            continue;

                        default:
                            // Not a known prefix, treat as plain text
                            break;
                    }
                }

                // FTS5 uses * as native prefix wildcard — keep as-is.
                // Keep OR, NOT, AND as-is for FTS5 boolean logic.
                // Keep quoted strings "..." as-is for FTS5 exact phrase matching.

                // Detect CamelCase tokens: short all-uppercase alphabetic tokens (2-4 chars)
                // that are NOT FTS5 boolean keywords. These are extracted for post-filter matching.
                if (IsCamelCaseToken(token))
                {
                    camelCaseTokens.Add(token);
                    continue;
                }

                plainParts.Add(token);
            }

            result.PlainTextQuery = string.Join(" ", plainParts);
            if (camelCaseTokens.Count > 0)
            {
                result.CamelCaseTokens = camelCaseTokens;
            }
            return result;
        }

        /// <summary>
        /// "[yyyyMMdd TO yyyyMMdd]" (either side may be *) → local start of the first day and end of
        /// the last day. False when the value is not such a range.
        /// </summary>
        private static bool TryParseDateRange(string value, out DateTime? from, out DateTime? to)
        {
            from = to = null;
            if (!value.StartsWith("[", StringComparison.Ordinal) || !value.EndsWith("]", StringComparison.Ordinal)) return false;
            var parts = value.Substring(1, value.Length - 2).Split(new[] { " TO " }, StringSplitOptions.None);
            if (parts.Length != 2) return false;

            bool Side(string text, out DateTime? day)
            {
                day = null;
                text = text.Trim();
                if (text == "*") return true;
                if (!DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
                day = DateTime.SpecifyKind(d, DateTimeKind.Local);
                return true;
            }

            if (!Side(parts[0], out var first) || !Side(parts[1], out var last)) return false;
            if (first == null && last == null) return false;
            from = first;
            to = last?.AddDays(1).AddTicks(-1);
            return true;
        }

        /// <summary>
        /// Returns true if the token is a short (2-4 chars) all-uppercase alphabetic string
        /// that is not an FTS5 boolean keyword (OR, NOT, AND).
        /// Such tokens are treated as CamelCase initials for post-filter matching.
        /// </summary>
        private static bool IsCamelCaseToken(string token)
        {
            if (token.Length < 2 || token.Length > 4)
                return false;

            // Must be all uppercase alphabetic characters
            for (int i = 0; i < token.Length; i++)
            {
                if (!char.IsLetter(token[i]) || !char.IsUpper(token[i]))
                    return false;
            }

            // Exclude FTS5 boolean keywords
            return !Fts5BooleanKeywords.Contains(token);
        }

        /// <summary>
        /// Splits the query into tokens respecting quoted strings.
        /// A quoted string "like this" is kept as a single token including the quotes.
        /// </summary>
        private static List<string> Tokenize(string query)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            bool inBrackets = false; // spec 040: date:[20260901 TO 20260930] is one token
            int i = 0;

            while (i < query.Length)
            {
                char c = query[i];

                if (c == '"')
                {
                    if (inQuotes)
                    {
                        // End of quoted string
                        sb.Append(c);
                        inQuotes = false;
                        i++;
                        continue;
                    }
                    else
                    {
                        // Check if this quote is part of a prefix:value (e.g., sql:"SELECT *")
                        // If sb has content and no space, keep building
                        inQuotes = true;
                        sb.Append(c);
                        i++;
                        continue;
                    }
                }

                if (!inQuotes && c == '[') inBrackets = true;
                else if (!inQuotes && c == ']') inBrackets = false;

                if (char.IsWhiteSpace(c) && !inQuotes && !inBrackets)
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }
                    i++;
                    continue;
                }

                sb.Append(c);
                i++;
            }

            if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
            }

            return tokens;
        }
    }

    /// <summary>
    /// Result of parsing an advanced search query. Contains extracted prefix filters
    /// and the remaining plain text query for FTS5 matching.
    /// </summary>
    internal sealed class ParsedSearch
    {
        /// <summary>Filter by server name (server: prefix).</summary>
        public string? ServerFilter { get; set; }

        /// <summary>Filter by database name (database: or db: prefix).</summary>
        public string? DatabaseFilter { get; set; }

        /// <summary>FTS5 query for SQL text content (sql: prefix).</summary>
        public string? SqlFilter { get; set; }

        /// <summary>Filter by tab/entry name (name: prefix).</summary>
        public string? NameFilter { get; set; }

        /// <summary>Filter by starred/favorite status (starred: prefix).</summary>
        public bool? StarredFilter { get; set; }

        /// <summary>Filter by open status (open: prefix).</summary>
        public bool? OpenFilter { get; set; }

        /// <summary>Spec 040: file path contains (path: prefix).</summary>
        public string? PathFilter { get; set; }

        /// <summary>Spec 040: ran on or after (date:[from TO …], local start of day).</summary>
        public DateTime? DateFrom { get; set; }

        /// <summary>Spec 040: ran on or before (date:[… TO to], local end of day).</summary>
        public DateTime? DateTo { get; set; }

        /// <summary>
        /// The remaining plain text query after prefix and CamelCase token extraction.
        /// Preserves FTS5 syntax: * for prefix wildcards, "..." for exact phrases,
        /// and OR/NOT/AND for boolean logic.
        /// </summary>
        public string PlainTextQuery { get; set; } = "";

        /// <summary>True if any prefix filter was found in the query.</summary>
        public bool HasPrefixes { get; set; }

        /// <summary>
        /// Short all-uppercase tokens (2-4 chars) detected as CamelCase initials.
        /// These are applied as in-memory post-filters after FTS5 returns results.
        /// For example, "PC" matches words like "ProductCategory" or "price_calculator".
        /// </summary>
        public List<string>? CamelCaseTokens { get; set; }
    }
}
