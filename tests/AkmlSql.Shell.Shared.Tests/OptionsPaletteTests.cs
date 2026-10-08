#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Shell.Shared.Commands;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Productivity.CommandPalette;
using Xunit;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T151, OPT-07, FR-053, research R7) — Options settings in the Command Palette:
    /// the catalog is read from the built Options pages; a toggle flips through the page's own
    /// Load/Save and changes only its own setting in config.json; any other option opens Options
    /// on its page and focuses its row (<c>OptionsCommand.ShowOptions(pageKey, agentId, focusLabel)</c>
    /// → <see cref="IOptionsDialog.InitialFocusLabel"/> → <see cref="SettingsWindow.ApplyInitialFocus"/>).
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public class OptionsPaletteTests : AppDataIsolatedTest
    {
        private const string NullabilityLabel = "Show nullability info";

        // A History row neither the plain-language pass (T159) nor the number-field pass (T161)
        // renames: it already reads as contracts/ui.md §4 words it.
        private const string HistoryLabel = "Group repeated runs of the same query";

        private readonly FakeRpcClientAccessor _rpc = new FakeRpcClientAccessor();

        public OptionsPaletteTests() : base("akml-options-palette-")
        {
            OptionsCommand.TestRpcAccessor = _rpc;
            OptionsCommand.ThemePreferenceOverride = _ => { };
        }

        public override void Dispose()
        {
            OptionsCommand.TestRpcAccessor = null;
            OptionsCommand.ThemePreferenceOverride = null;
            OptionsCommand.WindowFactoryOverride = null;
            base.Dispose();
        }

        // ── Catalog ────────────────────────────────────────────────────────────

        [StaFact]
        public void Catalog_lists_show_nullability_as_an_IntelliSense_toggle()
        {
            var catalog = SettingsWindow.BuildOptionsCatalog(new AppSettings());

            var entry = Assert.Single(catalog, e => e.Label == NullabilityLabel);
            Assert.Equal("Toggle", entry.Kind);
            Assert.Equal("IntelliSense", entry.PageKey);
            Assert.True(entry.IsToggle);
            Assert.False(string.IsNullOrEmpty(entry.PageDisplay));
            Assert.False(string.IsNullOrEmpty(entry.Description));
        }

        [StaFact]
        public void Catalog_leaves_out_the_AI_page_and_info_and_button_rows()
        {
            // The Options window's own search index has all three kinds of row, so the exclusions
            // below are real ones, not an empty catalog passing by accident.
            var index = SearchIndexOf(new AppSettings());
            Assert.Contains(index, e => e.PageKey == "AI Assistance");
            Assert.Contains(index, e => e.Kind == "Info");
            Assert.Contains(index, e => e.Kind == "Button");

            var catalog = SettingsWindow.BuildOptionsCatalog(new AppSettings());

            Assert.NotEmpty(catalog);
            Assert.DoesNotContain(catalog, e => e.PageKey == "AI Assistance");
            Assert.DoesNotContain(catalog, e => e.Kind == "Info" || e.Kind == "Button");
            // Everything else on the pages is there, under the label the page shows.
            var expected = index
                .Where(e => e.PageKey != "AI Assistance" && e.Kind != "Info" && e.Kind != "Button")
                .Select(e => e.PageKey + "|" + e.Label)
                .Distinct()
                .ToList();
            Assert.Equal(expected, catalog.Select(e => e.PageKey + "|" + e.Label).ToList());
        }

        [StaFact]
        public void Catalog_is_cached_for_the_session()
        {
            var first = SettingsWindow.BuildOptionsCatalog(new AppSettings());
            Assert.Same(first, SettingsWindow.BuildOptionsCatalog(new AppSettings()));

            SettingsWindow.InvalidateOptionsCatalog();
            Assert.NotSame(first, SettingsWindow.BuildOptionsCatalog(new AppSettings()));
        }

        // ── Palette entries ────────────────────────────────────────────────────

        [StaFact]
        public void A_palette_entry_is_named_by_page_and_label_under_Options()
        {
            var option = Nullability();
            var entry = new OptionPaletteEntry(option);

            Assert.Equal("opt:IntelliSense:" + NullabilityLabel, entry.Id);
            Assert.Equal(option.PageDisplay + " › " + NullabilityLabel, entry.Name);
            Assert.Equal("Options", entry.Category);
            Assert.True(OptionPaletteEntry.IsOptionId(entry.Id));

            entry.IsOn = true;
            Assert.Equal("On", entry.StateText);
            entry.IsOn = false;
            Assert.Equal("Off", entry.StateText);
        }

        [StaFact]
        public void Options_are_matched_from_two_characters()
        {
            var entries = CommandRegistry.GetOptionEntries();

            Assert.Empty(CommandRegistry.MatchOptions("n", entries));
            Assert.Contains(CommandRegistry.MatchOptions("nullab", entries), e => e.Option.Label == NullabilityLabel);
            Assert.Contains(CommandRegistry.MatchOptions("show null", entries), e => e.Option.Label == NullabilityLabel);
            Assert.DoesNotContain(CommandRegistry.MatchOptions("agent", entries), e => e.Option.PageKey == "AI Assistance");
        }

        [StaFact]
        public void Option_states_are_read_from_settings()
        {
            var entries = CommandRegistry.GetOptionEntries();
            var nullability = Assert.Single(entries, e => e.Option.Label == NullabilityLabel);

            var settings = new AppSettings();
            settings.IntelliSense.ShowNullability = true;
            CommandRegistry.RefreshOptionStates(entries, settings);
            Assert.True(nullability.IsOn);

            settings.IntelliSense.ShowNullability = false;
            CommandRegistry.RefreshOptionStates(entries, settings);
            Assert.False(nullability.IsOn);
        }

        // ── Toggling ───────────────────────────────────────────────────────────

        [StaFact]
        public void Toggling_from_the_palette_changes_only_that_setting_in_config_json()
        {
            ConfigManager.Save(new AppSettings());
            ConfigManager.Save(ConfigManager.Load()); // settle anything Load fills in, so only the toggle can differ
            var before = File.ReadAllText(Constants.ConfigFilePath);
            var wasOn = ConfigManager.Load().IntelliSense.ShowNullability;
            var entry = new OptionPaletteEntry(Nullability());

            var isOn = CommandPaletteViewModel.ToggleOption(entry);

            var after = File.ReadAllText(Constants.ConfigFilePath);
            Assert.Equal(!wasOn, isOn);
            Assert.Equal(!wasOn, entry.IsOn);
            Assert.Equal(!wasOn, ConfigManager.Load().IntelliSense.ShowNullability);
            var changed = Assert.Single(ChangedPaths(before, after));
            Assert.Equal("intellisense.shownullability", changed.ToLowerInvariant());
            // Saved the way OK saves: the engine is told to drop its settings cache.
            Assert.Single(_rpc.Notifications, n => n.MessageType == MessageTypes.AnalysisSettingsChanged);

            // And back again: nothing else drifted on the way.
            Assert.Equal(wasOn, CommandPaletteViewModel.ToggleOption(entry));
            Assert.Empty(ChangedPaths(before, File.ReadAllText(Constants.ConfigFilePath)));
        }

        // ── Opening Options at a row ───────────────────────────────────────────

        [StaFact]
        public void ShowOptions_passes_the_focus_label_to_the_window()
        {
            var dialogs = new List<FakeDialog>();
            OptionsCommand.WindowFactoryOverride = s =>
            {
                // The first window asks for a theme reopen; the reopened one cancels.
                var d = new FakeDialog(s, reopen: dialogs.Count == 0);
                dialogs.Add(d);
                return d;
            };

            OptionsCommand.RunOptionsLoop("History", null, HistoryLabel);

            Assert.Equal(2, dialogs.Count);
            Assert.Equal("History", dialogs[0].ShownPage);
            Assert.Equal(HistoryLabel, dialogs[0].FocusLabelWhenShown);
            // After a theme pick the reopened window stays where the user was.
            Assert.Null(dialogs[1].FocusLabelWhenShown);
        }

        [StaFact]
        public void The_window_selects_the_page_and_focuses_the_row_once_loaded()
        {
            var dialog = new SettingsWindow(new AppSettings()) { InitialFocusLabel = HistoryLabel };
            var window = dialog.TestBuildWindowForRenderTest(null);
            Assert.Null(dialog.FocusedRow); // nothing happens before the window loads

            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            Assert.Equal("History", dialog.CurrentPageKey);
            var row = Assert.IsAssignableFrom<FrameworkElement>(dialog.FocusedRow);
            Assert.Contains(LogicalTree.Descendants<TextBlock>(row), t => t.Text == HistoryLabel);
        }

        [StaFact]
        public void An_unknown_focus_label_just_opens_the_page()
        {
            var dialog = new SettingsWindow(new AppSettings()) { InitialFocusLabel = "No such option" };
            var window = dialog.TestBuildWindowForRenderTest("History");

            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            Assert.Equal("History", dialog.CurrentPageKey);
            Assert.Null(dialog.FocusedRow);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static OptionsCatalogEntry Nullability() =>
            Assert.Single(SettingsWindow.BuildOptionsCatalog(new AppSettings()), e => e.Label == NullabilityLabel);

        private sealed class IndexRow
        {
            public string PageKey = string.Empty;
            public string Label = string.Empty;
            public string Kind = string.Empty;
        }

        /// <summary>The Options window's private search index, read by reflection.</summary>
        private static List<IndexRow> SearchIndexOf(AppSettings settings)
        {
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();
            var index = (IEnumerable)typeof(SettingsWindow)
                .GetField("_searchIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            var rows = new List<IndexRow>();
            foreach (var e in index)
            {
                var t = e.GetType();
                rows.Add(new IndexRow
                {
                    PageKey = (string)t.GetProperty("PageKey")!.GetValue(e)!,
                    Label = (string)t.GetProperty("Label")!.GetValue(e)!,
                    Kind = (string)t.GetProperty("Kind")!.GetValue(e)!,
                });
            }
            return rows;
        }

        /// <summary>The dotted paths of every JSON value that differs between two documents.</summary>
        private static List<string> ChangedPaths(string beforeJson, string afterJson)
        {
            using var before = JsonDocument.Parse(beforeJson);
            using var after = JsonDocument.Parse(afterJson);
            var changed = new List<string>();
            Diff(before.RootElement, after.RootElement, string.Empty, changed);
            return changed;
        }

        private static void Diff(JsonElement a, JsonElement b, string path, List<string> changed)
        {
            if (a.ValueKind == JsonValueKind.Object && b.ValueKind == JsonValueKind.Object)
            {
                var names = a.EnumerateObject().Select(p => p.Name)
                    .Union(b.EnumerateObject().Select(p => p.Name));
                foreach (var name in names)
                {
                    var child = path.Length == 0 ? name : path + "." + name;
                    var hasA = a.TryGetProperty(name, out var va);
                    var hasB = b.TryGetProperty(name, out var vb);
                    if (hasA && hasB) Diff(va, vb, child, changed);
                    else changed.Add(child);
                }
                return;
            }
            if (a.ValueKind == JsonValueKind.Array && b.ValueKind == JsonValueKind.Array)
            {
                var la = a.EnumerateArray().ToList();
                var lb = b.EnumerateArray().ToList();
                if (la.Count != lb.Count) { changed.Add(path); return; }
                for (var i = 0; i < la.Count; i++) Diff(la[i], lb[i], path + "[" + i + "]", changed);
                return;
            }
            if (a.ValueKind != b.ValueKind || a.GetRawText() != b.GetRawText())
                changed.Add(path);
        }

        private sealed class FakeDialog : IOptionsDialog
        {
            private readonly bool _reopen;

            public FakeDialog(AppSettings settings, bool reopen)
            {
                WorkingCopy = settings;
                _reopen = reopen;
            }

            public string? InitialAgentId { get; set; }
            public string? InitialFocusLabel { get; set; }
            public string? FocusLabelWhenShown { get; private set; }
            public string? ShownPage { get; private set; }
            public bool ThemeChangeRequested { get; private set; }
            public AppSettings WorkingCopy { get; }
            public string? CurrentPageKey => ShownPage;
            public AppSettings GetSettings() => WorkingCopy;

            public bool ShowDialog(string? initialPageKey)
            {
                ShownPage = initialPageKey;
                FocusLabelWhenShown = InitialFocusLabel;
                ThemeChangeRequested = _reopen;
                return false;
            }
        }
    }
}
