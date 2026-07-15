#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Spec 030 T079 (FR-043) — Options UI for the alias-generation policy shipped in T035
    /// (<see cref="AliasOptionsSettings"/>): the include-AS toggle, the object→alias custom map,
    /// and the list of prefixes to ignore when deriving an alias. The map and prefix list are edited
    /// as plain multi-line text (one entry per line) so the page is fully self-contained — no
    /// host-owned CRUD modal — and round-trips through the standard Load/Save dispatch.
    /// </summary>
    internal sealed class AliasesPage : IPageBuilder
    {
        public string Key     => "Aliases";
        public string Display => "Inserted Code › Aliases";
        public string Title   => "Aliases";
        public string Help    => "Control how AKML SQL generates table aliases in completions and JOINs: the include-AS style, a custom map that forces a specific alias for named objects, and naming prefixes to strip before an alias is derived. These apply when alias generation is on (Suggestions › Behavior › Tables Alias).";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Alias generation");

            var (rowIncludeAs, chkIncludeAs) = ctx.Rows.AddToggle(panel,
                "Include the AS keyword",
                "Insert AS in generated aliases (Orders AS o) rather than the bare form (Orders o). Applies when alias generation is on (Suggestions › Behavior › Tables Alias).");
            ctx.RegisterSearch("Include the AS keyword", "Insert AS in generated aliases (Orders AS o vs Orders o)", "Toggle", rowIncludeAs);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Custom alias map");

            var (rowMap, txtMap) = ctx.Rows.AddMultilineTextInput(panel,
                "Object → alias (one per line, e.g. Customers = c)",
                "Force a specific alias for an object. One mapping per line as “object = alias”. Object names are matched case-insensitively.",
                height: 110);
            ctx.RegisterSearch("Custom alias map", "Force a specific alias for an object (object = alias, one per line)", "Text", rowMap);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Prefixes to ignore");

            var (rowPrefixes, txtPrefixes) = ctx.Rows.AddMultilineTextInput(panel,
                "Prefixes (one per line, e.g. tbl_, vw_)",
                "Strip these prefixes from an object name before deriving its alias (tbl_Orders → o). One prefix per line.",
                height: 90);
            ctx.RegisterSearch("Prefixes to ignore", "Strip these prefixes before deriving an alias (one per line)", "Text", rowPrefixes);

            return new AliasesControls(chkIncludeAs, txtMap, txtPrefixes);
        }
    }

    internal sealed class AliasesControls : IPageControls
    {
        private readonly CheckBox _includeAs;
        private readonly TextBox _mapText;
        private readonly TextBox _prefixesText;

        public AliasesControls(CheckBox includeAs, TextBox mapText, TextBox prefixesText)
        {
            _includeAs = includeAs;
            _mapText = mapText;
            _prefixesText = prefixesText;
        }

        public void Load(AppSettings settings)
        {
            var a = settings.IntelliSense.AliasOptions;
            _includeAs.IsChecked = a.IncludeAs;
            _mapText.Text = string.Join(Environment.NewLine,
                (a.ObjectAliasMap ?? new Dictionary<string, string>()).Select(kv => $"{kv.Key} = {kv.Value}"));
            _prefixesText.Text = string.Join(Environment.NewLine, a.PrefixesToIgnore ?? Array.Empty<string>());
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.AliasOptions.IncludeAs = _includeAs.IsChecked == true;

            // Parse "object = alias" lines into the map. Case-insensitive keys; last duplicate wins
            // (indexer assignment never throws); blank/malformed lines are skipped.
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in SplitLines(_mapText.Text))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();
                if (key.Length == 0 || val.Length == 0) continue;
                map[key] = val;
            }
            settings.IntelliSense.AliasOptions.ObjectAliasMap = map;

            settings.IntelliSense.AliasOptions.PrefixesToIgnore = SplitLines(_prefixesText.Text)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public void Reset(AppSettings defaults) => Load(defaults);

        private static IEnumerable<string> SplitLines(string? text)
            => (text ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
