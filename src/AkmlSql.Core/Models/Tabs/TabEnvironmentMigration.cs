using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Config;

namespace AkmlSql.Core.Models.Tabs
{
    /// <summary>
    /// Spec 040 (OPT-08, data-model §1.3) — gives every tab-colouring rule a named environment.
    /// Run by <c>ConfigManager.Load</c> (both overloads) and by the Queries › Color page before it
    /// shows a settings object; idempotent, so running it again changes nothing.
    /// <list type="number">
    ///   <item>No environments and no rules: the four defaults are seeded.</item>
    ///   <item>Otherwise each rule whose <see cref="ColoringRule.Environment"/> names no existing
    ///     environment is linked to the environment of its (<see cref="ColoringRule.Label"/>,
    ///     <see cref="ColoringRule.Color"/>) pair, created in rule order when missing. A label that
    ///     repeats with a different colour becomes <c>Label (2)</c>. For a pre-spec-040 config this
    ///     builds the whole list; stock rules give PRODUCTION, STAGING, DEV and AZURE.</item>
    /// </list>
    /// A rule's Label and Color are never changed here — the tab colouring, the History badge and
    /// the Safety checks keep behaving exactly as before. The Color page writes them from the
    /// environment when it saves (<see cref="WriteLabelsFromEnvironments"/>).
    /// </summary>
    public static class TabEnvironmentMigration
    {
        /// <summary>Name given to the environment of a rule that has no label.</summary>
        public const string UnnamedEnvironment = "Environment";

        /// <summary>Colour given to an environment built from a rule whose colour isn't a hex colour.</summary>
        public const string FallbackColor = "#808A99";

        /// <summary>Runs the migration on <paramref name="tabs"/> in place. Null is ignored.</summary>
        public static void Apply(TabSettings? tabs)
        {
            if (tabs == null) return;
            if (tabs.ColoringRules == null) tabs.ColoringRules = new List<ColoringRule>();
            if (tabs.Environments == null) tabs.Environments = new List<TabEnvironment>();
            tabs.Environments.RemoveAll(e => e == null);

            if (tabs.Environments.Count == 0 && !tabs.ColoringRules.Any(r => r != null))
            {
                tabs.Environments.AddRange(TabEnvironment.CreateDefaults());
                return;
            }

            var environments = tabs.Environments;
            // (label, colour) → environment, for the rules linked in this run: a pair whose label
            // was suffixed ("PRODUCTION (2)") must still find the environment it created.
            var byPair = new Dictionary<string, TabEnvironment>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in ColoringRuleOrdering.InEvaluationOrder(tabs.ColoringRules))
            {
                var linked = Find(environments, rule.Environment);
                if (linked != null)
                {
                    rule.Environment = linked.Name;
                    continue;
                }

                var label = NameFromLabel(rule.Label);
                var color = NormalizeColor(rule.Color);
                var key = label + "\n" + color;
                if (!byPair.TryGetValue(key, out var environment))
                {
                    environment = environments.FirstOrDefault(e =>
                        string.Equals((e.Name ?? string.Empty).Trim(), label, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((e.Color ?? string.Empty).Trim(), color, StringComparison.OrdinalIgnoreCase));
                    if (environment == null)
                    {
                        environment = new TabEnvironment { Name = UniqueName(environments, label), Color = color };
                        environments.Add(environment);
                    }
                    byPair[key] = environment;
                }
                rule.Environment = environment.Name;
            }
        }

        /// <summary>
        /// Writes each rule's <see cref="ColoringRule.Label"/> and <see cref="ColoringRule.Color"/>
        /// from the environment it names — Safety (the PROD check and <c>EnvironmentSeverity</c>)
        /// and the History badge read the label. A rule whose environment doesn't exist keeps both.
        /// </summary>
        public static void WriteLabelsFromEnvironments(IEnumerable<ColoringRule> rules, IReadOnlyList<TabEnvironment> environments)
        {
            if (rules == null || environments == null) return;
            foreach (var rule in rules)
            {
                if (rule == null) continue;
                var environment = Find(environments, rule.Environment);
                if (environment == null) continue;
                rule.Environment = environment.Name;
                rule.Label = environment.Name;
                rule.Color = environment.Color;
            }
        }

        /// <summary>The environment named <paramref name="name"/> (trimmed, case-insensitive), or null.</summary>
        public static TabEnvironment? Find(IEnumerable<TabEnvironment> environments, string? name)
        {
            var target = (name ?? string.Empty).Trim();
            if (target.Length == 0 || environments == null) return null;
            return environments.FirstOrDefault(e =>
                e != null && string.Equals((e.Name ?? string.Empty).Trim(), target, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A rule label as a valid environment name: trimmed, at most 40 characters, never empty.</summary>
        private static string NameFromLabel(string? label)
        {
            var name = (label ?? string.Empty).Trim();
            if (name.Length == 0) name = UnnamedEnvironment;
            return Truncate(name, EnvironmentValidator.MaxNameLength);
        }

        /// <summary><paramref name="name"/>, or <c>name (2)</c>, <c>name (3)</c> … — the first not yet used.</summary>
        private static string UniqueName(IReadOnlyList<TabEnvironment> environments, string name)
        {
            if (Find(environments, name) == null) return name;
            for (var n = 2; ; n++)
            {
                var suffix = " (" + n + ")";
                var candidate = Truncate(name, EnvironmentValidator.MaxNameLength - suffix.Length).TrimEnd() + suffix;
                if (Find(environments, candidate) == null) return candidate;
            }
        }

        /// <summary>
        /// A rule colour as <c>#RRGGBB</c>: kept as written when it already is, <c>RRGGBB</c> gains
        /// its <c>#</c>, <c>#AARRGGBB</c> loses the alpha, anything else becomes <see cref="FallbackColor"/>.
        /// </summary>
        internal static string NormalizeColor(string? color)
        {
            var value = (color ?? string.Empty).Trim();
            if (EnvironmentValidator.IsValidColor(value)) return value;
            if (value.Length == 6 && EnvironmentValidator.IsValidColor("#" + value)) return "#" + value;
            if (value.Length == 9 && value[0] == '#' &&
                EnvironmentValidator.IsValidColor("#" + value.Substring(1, 6)) &&
                EnvironmentValidator.IsValidColor("#" + value.Substring(3)))
                return "#" + value.Substring(3);
            return FallbackColor;
        }

        private static string Truncate(string value, int length)
            => value.Length <= length ? value : value.Substring(0, length);
    }
}
