#nullable enable
using System;
using System.Collections.Generic;

namespace AkmlSql.Core.Models.Tabs
{
    /// <summary>
    /// Pure matching logic for environment coloring rules. Extracted from the
    /// shell-side <c>EnvironmentDetector</c> so it can be unit-tested without VS SDK dependencies.
    /// </summary>
    public static class EnvironmentMatcher
    {
        /// <summary>Match target value for rules that match against the server name.</summary>
        public const string MatchTargetServerName = "serverName";

        /// <summary>
        /// Match target value for rules that match against the connected database name,
        /// regardless of which server it lives on ("database-on-any-server", FR-038).
        /// </summary>
        public const string MatchTargetDatabase = "database";

        /// <summary>
        /// Tests <paramref name="serverName"/> against each server-name rule in order.
        /// Database-target rules are skipped (no database name is available).
        /// Returns the first matching rule, or <c>null</c> if none match.
        /// Rules must be pre-sorted by <see cref="EnvironmentRule.Order"/> ascending.
        /// </summary>
        public static EnvironmentRule? Match(IReadOnlyList<EnvironmentRule> rules, string? serverName)
            => Match(rules, serverName, null);

        /// <summary>
        /// Tests a connection against each rule in order. A rule with
        /// <see cref="MatchTargetDatabase"/> is evaluated against <paramref name="databaseName"/>
        /// (regardless of the server, i.e. "database-on-any-server"); any other rule is
        /// evaluated against <paramref name="serverName"/>.
        /// Returns the first matching rule, or <c>null</c> if none match.
        /// Rules must be pre-sorted by <see cref="EnvironmentRule.Order"/> ascending.
        /// </summary>
        public static EnvironmentRule? Match(IReadOnlyList<EnvironmentRule> rules, string? serverName, string? databaseName)
        {
            if (rules == null)
                return null;

            foreach (var rule in rules)
            {
                if (string.Equals(rule.MatchTarget, MatchTargetDatabase, StringComparison.OrdinalIgnoreCase))
                {
                    // Database rule: match against the database name on any server.
                    // Prefer DatabaseName (the dedicated db-pattern field) over Pattern so that
                    // ColoringRule.DatabaseName is honoured; fall back to Pattern for rules
                    // constructed without a dedicated DatabaseName (backward compatibility).
                    var dbPattern = string.IsNullOrEmpty(rule.DatabaseName) ? rule.Pattern : rule.DatabaseName;
                    if (!string.IsNullOrWhiteSpace(databaseName) && MatchesPattern(dbPattern, databaseName!))
                        return rule;
                }
                else if (string.Equals(rule.MatchTarget, MatchTargetServerName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(serverName) && MatchesPattern(rule.Pattern, serverName!))
                        return rule;
                }
            }

            return null;
        }

        /// <summary>
        /// Splits a comma-separated pattern string into sub-patterns and returns <c>true</c>
        /// if any sub-pattern matches <paramref name="value"/>.
        /// <para>
        /// Glob matching supports <c>*</c> at the start and/or end:
        /// <list type="bullet">
        ///   <item><c>"*PROD*"</c> — contains "PROD"</item>
        ///   <item><c>"*.database.windows.net"</c> — ends with ".database.windows.net"</item>
        ///   <item><c>"DEV*"</c> — starts with "DEV"</item>
        ///   <item><c>"localhost"</c> — exact match</item>
        /// </list>
        /// All comparisons are case-insensitive.
        /// </para>
        /// </summary>
        public static bool MatchesPattern(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            var subPatterns = pattern.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in subPatterns)
            {
                var sub = raw.Trim();
                if (sub.Length == 0) continue;

                if (GlobMatch(sub, value))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Simple glob matcher: supports <c>*</c> at start and/or end of <paramref name="glob"/>.
        /// Case-insensitive.
        /// </summary>
        public static bool GlobMatch(string glob, string value)
        {
            bool startsWithWild = glob.StartsWith("*", StringComparison.Ordinal);
            bool endsWithWild   = glob.EndsWith("*", StringComparison.Ordinal);

            // Strip wildcards to get the literal core.
            string core = glob;
            if (startsWithWild) core = core.Substring(1);
            if (endsWithWild && core.Length > 0) core = core.Substring(0, core.Length - 1);

            if (core.Length == 0)
            {
                // Pattern was "*" or "**" — matches everything.
                return true;
            }

            if (startsWithWild && endsWithWild)
            {
                // *PROD* — value must contain core
                return value.IndexOf(core, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (startsWithWild)
            {
                // *.database.windows.net — value must end with core
                return value.EndsWith(core, StringComparison.OrdinalIgnoreCase);
            }

            if (endsWithWild)
            {
                // DEV* — value must start with core
                return value.StartsWith(core, StringComparison.OrdinalIgnoreCase);
            }

            // No wildcards — exact match
            return string.Equals(core, value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
