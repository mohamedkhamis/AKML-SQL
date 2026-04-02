#nullable enable
using System;
using System.Collections.Generic;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Tabs;
using Serilog;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// Matches a server name (or other connection property) against the configured
    /// <see cref="ColoringRule"/> list and returns the first matching
    /// <see cref="EnvironmentRule"/>.
    /// <para>
    /// Thread-safe: rules are loaded once during <see cref="Initialize"/> and stored
    /// in an immutable array. <see cref="Match"/> can be called from any thread.
    /// </para>
    /// </summary>
    internal static class EnvironmentDetector
    {
        /// <summary>Immutable, sorted array of rules — set once during <see cref="Initialize"/>.</summary>
        private static EnvironmentRule[] _rules = Array.Empty<EnvironmentRule>();

        /// <summary>
        /// Loads environment rules from the current <see cref="TabSettings.ColoringRules"/> config.
        /// Should be called once during package initialization. Subsequent calls replace the rule set.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                var settings = ConfigManager.Load();
                var configRules = settings.Tabs?.ColoringRules;

                if (configRules == null || configRules.Count == 0)
                {
                    _rules = Array.Empty<EnvironmentRule>();
                    Log.Information("EnvironmentDetector: no coloring rules configured");
                    return;
                }

                var rules = new List<EnvironmentRule>(configRules.Count);
                foreach (var cr in configRules)
                {
                    rules.Add(new EnvironmentRule(
                        cr.Order,
                        cr.Pattern,
                        cr.MatchTarget,
                        cr.Color,
                        cr.Label));
                }

                // Sort by Order ascending so lowest-order rule wins on first match.
                rules.Sort((a, b) => a.Order.CompareTo(b.Order));
                _rules = rules.ToArray();

                Log.Information("EnvironmentDetector: loaded {Count} coloring rule(s)", _rules.Length);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "EnvironmentDetector: failed to load coloring rules");
                _rules = Array.Empty<EnvironmentRule>();
            }
        }

        /// <summary>
        /// Tests <paramref name="serverName"/> against each rule in order.
        /// Returns the first matching <see cref="EnvironmentRule"/>, or <c>null</c> if none match.
        /// </summary>
        /// <param name="serverName">
        /// The SQL Server instance name or hostname (e.g. <c>"SQLPROD01"</c>,
        /// <c>"myserver.database.windows.net"</c>).
        /// </param>
        public static EnvironmentRule? Match(string? serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
                return null;

            // Snapshot the immutable array for lock-free read.
            var rules = _rules;

            foreach (var rule in rules)
            {
                // Currently only "serverName" matchTarget is supported.
                if (!string.Equals(rule.MatchTarget, "serverName", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (MatchesPattern(rule.Pattern, serverName))
                    return rule;
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
        private static bool MatchesPattern(string pattern, string value)
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
        private static bool GlobMatch(string glob, string value)
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
