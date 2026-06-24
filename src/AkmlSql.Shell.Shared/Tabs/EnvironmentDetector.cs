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
                        cr.DatabaseName,
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
        /// Reloads coloring rules from the current config. Call after the user saves
        /// settings to pick up changes without restarting the IDE.
        /// Thread-safe: the new rule array is assigned atomically.
        /// </summary>
        public static void Reload()
        {
            Initialize(); // Same logic — loads from config and replaces _rules
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
            return EnvironmentMatcher.Match(_rules, serverName);
        }

        /// <summary>
        /// Tests a (<paramref name="serverName"/>, <paramref name="databaseName"/>) pair against each
        /// rule in order — the database-aware overload (spec 030 T071). A rule whose
        /// <see cref="ColoringRule.MatchTarget"/> is <c>Database</c> matches on the database name; the
        /// default <c>Server</c> rules still match on the server. Returns the first matching rule, or
        /// <c>null</c> if none match.
        /// </summary>
        public static EnvironmentRule? Match(string? serverName, string? databaseName)
        {
            return EnvironmentMatcher.Match(_rules, serverName, databaseName);
        }
    }
}
