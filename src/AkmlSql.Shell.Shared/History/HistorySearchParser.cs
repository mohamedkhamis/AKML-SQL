#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Parses advanced search syntax for the SQL History search bar.
    /// Supports prefix filters (server:, database:, db:, sql:, name:, starred:, open:),
    /// quoted phrases, wildcard characters (* -> %, ? -> _), and boolean keywords (OR, NOT).
    /// </summary>
    internal static class HistorySearchParser
    {
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
                            // name: filter maps to tab_title search — append to plain text
                            // since the engine's HistorySearchRequest doesn't have a TabTitle filter field.
                            plainParts.Add(value);
                            result.HasPrefixes = true;
                            continue;

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

                // FTS5 uses * as native prefix wildcard — don't convert to %.
                // ? has no FTS5 equivalent — leave as literal.
                // Keep OR and NOT as-is for boolean logic.
                // Keep quoted strings as-is.
                plainParts.Add(token);
            }

            result.PlainTextQuery = string.Join(" ", plainParts);
            return result;
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

                if (char.IsWhiteSpace(c) && !inQuotes)
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

        /// <summary>
        /// The remaining plain text query after prefix extraction.
        /// Wildcard characters are converted: * -> %, ? -> _.
        /// </summary>
        public string PlainTextQuery { get; set; } = "";

        /// <summary>True if any prefix filter was found in the query.</summary>
        public bool HasPrefixes { get; set; }
    }
}
