using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Core.Models.Tabs
{
    /// <summary>
    /// How an environment's name meets the execution Safety checks. Safety keys on a rule's label —
    /// <c>Safety.EnvironmentSeverity[label]</c> and "PROD" in the label — and the label is the
    /// environment's name, which Queries › Color › Edit environments lets the user change.
    /// </summary>
    public static class EnvironmentSafety
    {
        /// <summary>The severity that makes a query on that environment type the server name to run.</summary>
        public const string TypeServerName = "TypeServerName";

        /// <summary>
        /// Whether a query on the environment <paramref name="label"/> is on production: its name
        /// says PROD, or its severity asks for the server name to be typed — so PRODUCTION renamed
        /// "Live" is still production.
        /// </summary>
        public static bool IsProduction(string? label, IReadOnlyDictionary<string, string>? severity)
        {
            if (string.IsNullOrWhiteSpace(label)) return false;
            if (label!.IndexOf("PROD", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return severity != null
                   && TryGet(severity, label, out var level)
                   && string.Equals(level, TypeServerName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Moves each renamed environment's severity to its new name (old name → new name in
        /// <paramref name="renames"/>), so a rename keeps the environment's protection. Names are
        /// unique, so a severity already stored under a new name belonged to no environment and is
        /// replaced. Returns how many moved.
        /// </summary>
        public static int FollowRenames(IDictionary<string, string> severity, IReadOnlyDictionary<string, string> renames)
        {
            if (severity == null || renames == null) return 0;
            // Read every old name's severity before writing any: renames can swap names.
            var moves = renames
                .Where(r => !string.IsNullOrWhiteSpace(r.Key) && !string.IsNullOrWhiteSpace(r.Value)
                            && !string.Equals(r.Key.Trim(), r.Value.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(r => (From: r.Key.Trim(), To: r.Value.Trim(), Found: TryGet(severity, r.Key.Trim(), out var level), Level: level))
                .Where(m => m.Found)
                .ToList();
            foreach (var move in moves) Remove(severity, move.From);
            var moved = 0;
            foreach (var move in moves)
            {
                Remove(severity, move.To);
                severity[move.To] = move.Level!;
                moved++;
            }
            return moved;
        }

        // The settings dictionary is case-insensitive when built in code, but one read from JSON
        // may not be: look names up without regard to case either way.
        private static bool TryGet(IEnumerable<KeyValuePair<string, string>> severity, string name, out string? level)
        {
            foreach (var pair in severity)
            {
                if (string.Equals(pair.Key?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    level = pair.Value;
                    return true;
                }
            }
            level = null;
            return false;
        }

        private static void Remove(IDictionary<string, string> severity, string name)
        {
            foreach (var key in severity.Keys.Where(k => string.Equals(k?.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList())
                severity.Remove(key);
        }
    }
}
