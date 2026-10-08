using System;
using System.Collections.Generic;
using AkmlSql.Core.Config;

namespace AkmlSql.Core.Models.Tabs
{
    /// <summary>
    /// Spec 040 (OPT-08, FR-054, data-model §1.2 "Validation") — the pure checks behind the
    /// Edit environments dialog: names unique (case-insensitive) and 1–40 characters, colours
    /// <c>#RRGGBB</c>, and an environment that rules use can't be deleted.
    /// </summary>
    public static class EnvironmentValidator
    {
        /// <summary>Longest environment name, in characters.</summary>
        public const int MaxNameLength = 40;

        /// <summary>True for exactly <c>#RRGGBB</c> (hex digits in either case), nothing around it.</summary>
        public static bool IsValidColor(string? color)
        {
            if (color == null || color.Length != 7 || color[0] != '#') return false;
            for (var i = 1; i < 7; i++)
            {
                var c = color[i];
                var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>
        /// Null when <paramref name="name"/> (trimmed) is 1–<see cref="MaxNameLength"/> characters,
        /// otherwise the message to show.
        /// </summary>
        public static string? ValidateName(string? name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) return "Every environment needs a name.";
            if (trimmed.Length > MaxNameLength)
                return $"'{trimmed}' is too long. Use at most {MaxNameLength} characters.";
            return null;
        }

        /// <summary>
        /// Every problem with <paramref name="environments"/>, in list order; empty when they can be
        /// saved. Names are compared trimmed and case-insensitively. At least one environment is
        /// required: an empty list would be re-seeded with the defaults on the next load.
        /// </summary>
        public static IReadOnlyList<string> Validate(IReadOnlyList<TabEnvironment> environments)
        {
            var errors = new List<string>();
            if (environments == null || environments.Count == 0)
            {
                errors.Add("Add at least one environment.");
                return errors;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var env in environments)
            {
                var name = (env?.Name ?? string.Empty).Trim();
                var nameError = ValidateName(name);
                if (nameError != null)
                {
                    errors.Add(nameError);
                }
                else if (!seen.Add(name) && reported.Add(name))
                {
                    errors.Add($"There is more than one environment named '{name}'. Names must be unique.");
                }

                if (!IsValidColor(env?.Color))
                {
                    var who = name.Length == 0 ? "An environment" : $"'{name}'";
                    errors.Add($"{who} has no valid color. Use #RRGGBB.");
                }
            }
            return errors;
        }

        /// <summary>
        /// False when any rule uses the environment <paramref name="name"/> (case-insensitive);
        /// <paramref name="usedBy"/> then lists those rules, as <see cref="DescribeRule"/> writes
        /// them, for the refusal message.
        /// </summary>
        public static bool CanDelete(string name, IEnumerable<ColoringRule> rules, out IReadOnlyList<string> usedBy)
        {
            var patterns = new List<string>();
            var target = (name ?? string.Empty).Trim();
            if (rules != null && target.Length > 0)
            {
                foreach (var rule in rules)
                {
                    if (rule == null) continue;
                    if (string.Equals((rule.Environment ?? string.Empty).Trim(), target, StringComparison.OrdinalIgnoreCase))
                        patterns.Add(DescribeRule(rule));
                }
            }
            usedBy = patterns;
            return patterns.Count == 0;
        }

        /// <summary>The message shown when a delete is refused.</summary>
        public static string DeleteRefusedMessage(string name, IReadOnlyList<string> usedBy)
            => $"'{name}' can't be deleted because these rules use it: {string.Join(", ", usedBy)}. " +
               "Change their environment or remove them first.";

        /// <summary>
        /// A rule as the user typed it: the server / group pattern, then <c>/ database</c> when the
        /// rule also names a database. A database rule ("this database on any server") reads
        /// <c>* / database</c>.
        /// </summary>
        public static string DescribeRule(ColoringRule rule)
        {
            if (rule == null) return string.Empty;
            var pattern = (rule.Pattern ?? string.Empty).Trim();
            var database = (rule.DatabaseName ?? string.Empty).Trim();
            if (string.Equals(rule.MatchTarget, EnvironmentMatcher.MatchTargetDatabase, StringComparison.OrdinalIgnoreCase))
                return "* / " + (database.Length > 0 ? database : pattern);
            return database.Length > 0 ? pattern + " / " + database : pattern;
        }
    }
}
