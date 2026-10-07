#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// The "what does each value do" cards of the Format Styles window, without WPF: which SQL to
    /// format with which settings for each value of an option, and which lines a value changed.
    /// The engine sends each option's example (schema 2002+: a query, the settings it needs, the
    /// values to show); the window formats one request per value with the working style.
    /// </summary>
    internal static class FormatStylesExamples
    {
        internal sealed class Card
        {
            public object? Value { get; set; }
            public bool IsDefault { get; set; }
            /// <summary>Index into <see cref="Plan.Requests"/> of this value's output.</summary>
            public int Output { get; set; }
            /// <summary>Index into <see cref="Plan.Requests"/> of the default's output on the same query.</summary>
            public int Baseline { get; set; }
            /// <summary>The query's title when this value is shown on a query of its own, else null.</summary>
            public string? OwnQueryTitle { get; set; }
            /// <summary>Settings this value needs beyond the example's own (setting id → value).</summary>
            public List<KeyValuePair<string, object?>> OwnWith { get; } = new List<KeyValuePair<string, object?>>();
        }

        internal sealed class Plan
        {
            public string QueryTitle { get; set; } = string.Empty;
            /// <summary>The settings the example needs besides the option itself (setting id → value).</summary>
            public List<KeyValuePair<string, object?>> With { get; } = new List<KeyValuePair<string, object?>>();
            public List<(string Sql, IReadOnlyList<KeyValuePair<string, object?>> Settings)> Requests { get; } =
                new List<(string, IReadOnlyList<KeyValuePair<string, object?>>)>();
            public List<Card> Cards { get; } = new List<Card>();
        }

        /// <summary>
        /// The requests and cards for <paramref name="setting"/>'s example, or null when the engine
        /// sent none (an older engine, or the AKML settings model). Identical requests are sent once:
        /// the default's card doubles as every other card's baseline.
        /// </summary>
        internal static Plan? For(FormatStylesSchemaModel.Model model, FormatSettingNode setting)
        {
            var example = setting.Example;
            if (example == null || example.Values.Count < 2) return null;
            var page = model.GroupOf(setting.Id);
            var defaultValue = example.Values[0];

            var plan = new Plan { QueryTitle = TitleOf(model, example.Target.QueryId, page) };
            plan.With.AddRange(example.Target.With.Where(w => !string.Equals(w.Key, setting.Id, StringComparison.Ordinal)));
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            int Add(FormatStylesSchemaModel.ExampleTarget target, object? value)
            {
                var sql = model.SqlFor(target.QueryId, page);
                if (string.IsNullOrWhiteSpace(sql)) return -1;
                var settings = new List<KeyValuePair<string, object?>>(target.With) { new KeyValuePair<string, object?>(setting.Id, value) };
                var key = target.QueryId + "\n" + string.Join("\n", settings.Select(s => s.Key + "=" + FormatStylesSchemaModel.ValueKey(s.Value)));
                if (!seen.TryGetValue(key, out var index))
                {
                    index = plan.Requests.Count;
                    plan.Requests.Add((sql!, settings));
                    seen[key] = index;
                }
                return index;
            }

            var mainWith = new HashSet<string>(example.Target.With.Select(w => w.Key + "=" + FormatStylesSchemaModel.ValueKey(w.Value)), StringComparer.Ordinal);
            foreach (var value in example.Values)
            {
                var target = example.TargetFor(value);
                var output = Add(target, value);
                if (output < 0) continue;
                var card = new Card
                {
                    Value = value,
                    IsDefault = FormatStylesSchemaModel.ValueKey(value) == FormatStylesSchemaModel.ValueKey(defaultValue),
                    Output = output,
                    Baseline = Add(target, defaultValue),
                    OwnQueryTitle = string.Equals(target.QueryId, example.Target.QueryId, StringComparison.Ordinal) ? null : TitleOf(model, target.QueryId, page),
                };
                card.OwnWith.AddRange(target.With.Where(w =>
                    !string.Equals(w.Key, setting.Id, StringComparison.Ordinal) && !mainWith.Contains(w.Key + "=" + FormatStylesSchemaModel.ValueKey(w.Value))));
                plan.Cards.Add(card);
            }
            return plan.Cards.Count < 2 ? null : plan;
        }

        /// <summary>
        /// The tab width a request was formatted with — its own "spaces per tab" setting when it
        /// sets one (that option's cards, and examples that need a width), else <paramref name="working"/>.
        /// </summary>
        internal static int TabSizeOf(IReadOnlyList<KeyValuePair<string, object?>> settings, string tabSizeId, int working)
        {
            foreach (var s in settings)
            {
                if (!string.Equals(s.Key, tabSizeId, StringComparison.Ordinal) || !(s.Value is IConvertible c) || s.Value is string || s.Value is bool) continue;
                try { return Math.Max(1, Math.Min(16, Convert.ToInt32(c, System.Globalization.CultureInfo.InvariantCulture))); }
                catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException) { }
            }
            return working;
        }

        private static string TitleOf(FormatStylesSchemaModel.Model model, string queryId, FormatStylesSchemaModel.Group? page) =>
            queryId == FormatStylesSchemaModel.PageSampleQueryId
                ? (page?.DisplayName ?? "This page") + " sample"
                : model.ExampleQueries.TryGetValue(queryId, out var q) ? q.Name : queryId;

        /// <summary>
        /// 0-based lines of <paramref name="text"/> that are not in a longest common subsequence with
        /// <paramref name="baseline"/>: the lines a value changed or added. Unlike a line-by-line
        /// comparison, one inserted line does not mark every line below it.
        /// </summary>
        internal static int[] ChangedLines(string? baseline, string text)
        {
            if (baseline == null || string.Equals(baseline, text, StringComparison.Ordinal)) return new int[0];
            var a = Lines(baseline);
            var b = Lines(text);
            var n = a.Length;
            var m = b.Length;
            // Examples are short (tens of lines); past a few hundred, mark nothing rather than burn time.
            if ((long)n * m > 400_000) return new int[0];

            var lcs = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            var kept = new bool[m];
            for (int i = 0, j = 0; i < n && j < m;)
            {
                if (string.Equals(a[i], b[j], StringComparison.Ordinal)) { kept[j] = true; i++; j++; }
                else if (lcs[i + 1, j] >= lcs[i, j + 1]) i++;
                else j++;
            }
            var changed = new List<int>();
            for (var k = 0; k < m; k++) if (!kept[k]) changed.Add(k);
            return changed.ToArray();
        }

        private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');
    }
}
