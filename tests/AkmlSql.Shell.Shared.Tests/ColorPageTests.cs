#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Tabs;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T165/T166, OPT-08, FR-054) — Options › Queries › Color: the rule grid loads and
    /// saves rules and environments with the page; an untouched config saves back unchanged (Safety
    /// and the History badge key on Label); edited rows become server, server + database or
    /// database rules; ↑/↓ renumber Order; every saved rule takes Label and Color from its
    /// environment; Edit environments refuses to delete an environment rules use.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public sealed class ColorPageTests
    {
        private static (TabsControls Controls, List<string> Search) Build(AppSettings settings)
        {
            var (controls, search, _) = Build(settings, PageTheme.Light);
            return (controls, search);
        }

        private static (TabsControls Controls, List<string> Search, StackPanel Panel) Build(AppSettings settings, PageTheme theme)
        {
            var search = new List<string>();
            var panel = new StackPanel();
            var ctx = new PageContext(theme, settings, new RowFactory(theme),
                (label, _, __, ___) => search.Add(label));
            var controls = (TabsControls)new TabsPage().Build(panel, ctx);
            controls.Load(settings);
            return (controls, search, panel);
        }

        private static void Layout(FrameworkElement root)
        {
            root.Measure(new Size(760, 1000));
            root.Arrange(new Rect(0, 0, 760, 1000));
            root.UpdateLayout();
        }

        private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var nested in VisualDescendants<T>(child)) yield return nested;
            }
        }

        private static AppSettings Migrated()
        {
            var settings = new AppSettings();
            TabEnvironmentMigration.Apply(settings.Tabs); // what ConfigManager.Load does
            return settings;
        }

        private static string Describe(ColoringRule r)
            => string.Join("|", r.Order, r.Pattern, r.MatchTarget, r.DatabaseName, r.Label, r.Color, r.Environment);

        [StaFact]
        public void The_page_is_Queries_Color_with_sentence_case_rows()
        {
            var page = new TabsPage();
            Assert.Equal("Queries › Color", page.Display);
            Assert.Equal("Color", page.Title);

            var (_, search) = Build(Migrated());
            Assert.Equal(new[] { "Enable tab coloring", "Use gradient colors", "Tab color rules", "Edit environments", "Custom window title template" }, search);
        }

        [StaFact]
        public void An_untouched_config_saves_back_unchanged()
        {
            var settings = Migrated();
            var before = settings.Tabs.ColoringRules.Select(Describe).ToList();
            var environments = settings.Tabs.Environments.Select(e => e.Name + e.Color).ToList();
            var (controls, _) = Build(settings);

            controls.Save(settings);

            Assert.Equal(before, settings.Tabs.ColoringRules.Select(Describe));
            Assert.Equal(environments, settings.Tabs.Environments.Select(e => e.Name + e.Color));
        }

        [StaFact]
        public void A_fresh_settings_object_shows_the_four_default_environments()
        {
            var (controls, _) = Build(new AppSettings());

            Assert.Equal(new[] { "PRODUCTION", "STAGING", "DEV", "AZURE" }, controls.Rules.EnvironmentNames);
            Assert.Equal(4, controls.Rules.Rows.Count);
            Assert.All(controls.Rules.Rows, r => Assert.False(string.IsNullOrEmpty(r.Environment)));
        }

        [StaFact]
        public void An_untouched_database_rule_keeps_its_match_target_and_pattern()
        {
            var settings = Migrated();
            settings.Tabs.ColoringRules.Insert(0, new ColoringRule
            {
                Order = -1, Pattern = "ProdDb", MatchTarget = EnvironmentMatcher.MatchTargetDatabase,
                Color = "#FF4444", Label = "PRODUCTION", Environment = "PRODUCTION",
            });
            var (controls, _) = Build(settings);

            var row = controls.Rules.Rows[0];
            Assert.Equal("*", row.Server);
            Assert.Equal("ProdDb", row.Database);

            controls.Save(settings);
            var saved = settings.Tabs.ColoringRules[0];
            Assert.Equal(EnvironmentMatcher.MatchTargetDatabase, saved.MatchTarget);
            Assert.Equal("ProdDb", saved.Pattern);
            Assert.Equal(string.Empty, saved.DatabaseName);
            Assert.Equal(0, saved.Order);
        }

        [StaFact]
        public void Edited_rows_become_server_server_and_database_or_database_rules()
        {
            var settings = Migrated();
            settings.Tabs.ColoringRules.Clear();
            var (controls, _) = Build(settings);

            var server = controls.Rules.AddRule();
            server.Server = "SQLPROD*";
            var serverAndDatabase = controls.Rules.AddRule();
            serverAndDatabase.Server = "SQLPROD*";
            serverAndDatabase.Database = "Sales";
            var anyServer = controls.Rules.AddRule();
            anyServer.Server = "*";
            anyServer.Database = "Orders";
            var blank = controls.Rules.AddRule(); // neither server nor database: dropped

            controls.Save(settings);
            var rules = settings.Tabs.ColoringRules;

            Assert.Equal(3, rules.Count);
            Assert.Equal(new[] { 0, 1, 2 }, rules.Select(r => r.Order));
            Assert.Equal(EnvironmentMatcher.MatchTargetServerName, rules[0].MatchTarget);
            Assert.Equal("SQLPROD*", rules[0].Pattern);
            Assert.Equal(string.Empty, rules[0].DatabaseName);
            Assert.Equal(EnvironmentMatcher.MatchTargetServerName, rules[1].MatchTarget);
            Assert.Equal("Sales", rules[1].DatabaseName);
            Assert.Equal(EnvironmentMatcher.MatchTargetDatabase, rules[2].MatchTarget);
            Assert.Equal("Orders", rules[2].DatabaseName);
            // A new rule starts on the first environment and takes its label and colour.
            Assert.All(rules, r => Assert.Equal("PRODUCTION", r.Environment));
            Assert.All(rules, r => Assert.Equal("PRODUCTION", r.Label));
            Assert.All(rules, r => Assert.Equal("#FF4444", r.Color));
        }

        [StaFact]
        public void Choosing_an_environment_writes_its_label_and_color_on_save()
        {
            var settings = Migrated();
            var (controls, _) = Build(settings);

            controls.Rules.Rows[0].Environment = "DEV"; // the *PROD* rule

            controls.Save(settings);
            Assert.Equal("DEV", settings.Tabs.ColoringRules[0].Label);
            Assert.Equal("#44BB44", settings.Tabs.ColoringRules[0].Color);
            Assert.Equal("*PROD*,*LIVE*", settings.Tabs.ColoringRules[0].Pattern);
        }

        [StaFact]
        public void Up_and_down_reorder_and_renumber()
        {
            var settings = Migrated();
            var (controls, _) = Build(settings);
            var grid = controls.Rules;

            grid.List.SelectedIndex = 2; // DEV
            grid.MoveSelected(-1);
            Assert.Equal(1, grid.List.SelectedIndex);
            grid.List.SelectedIndex = 0; // PRODUCTION: up is ignored at the top
            grid.MoveSelected(-1);

            controls.Save(settings);
            Assert.Equal(new[] { "PRODUCTION", "DEV", "STAGING", "AZURE" }, settings.Tabs.ColoringRules.Select(r => r.Label));
            Assert.Equal(new[] { 0, 1, 2, 3 }, settings.Tabs.ColoringRules.Select(r => r.Order));
        }

        [StaFact]
        public void Remove_deletes_the_selected_rule()
        {
            var settings = Migrated();
            var (controls, _) = Build(settings);

            controls.Rules.List.SelectedIndex = 1;
            controls.Rules.RemoveSelected();

            controls.Save(settings);
            Assert.Equal(new[] { "PRODUCTION", "DEV", "AZURE" }, settings.Tabs.ColoringRules.Select(r => r.Label));
        }

        [StaFact]
        public void A_renamed_environment_is_followed_by_its_rules()
        {
            var settings = Migrated();
            var (controls, _) = Build(settings);
            var environments = controls.Rules.Environments.Select(e => e.Clone()).ToList();
            environments[0].Name = "Production";
            environments[0].Color = "#E91E63";
            var renames = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["PRODUCTION"] = "Production", ["STAGING"] = "STAGING", ["DEV"] = "DEV", ["AZURE"] = "AZURE",
            };

            controls.Rules.ApplyEnvironmentEdit(new EnvironmentEditResult(environments, renames, gradientColors: true));
            controls.Save(settings);

            Assert.Equal("Production", settings.Tabs.ColoringRules[0].Environment);
            Assert.Equal("Production", settings.Tabs.ColoringRules[0].Label);
            Assert.Equal("#E91E63", settings.Tabs.ColoringRules[0].Color);
            Assert.Equal("Production", settings.Tabs.Environments[0].Name);
            Assert.True(settings.Tabs.GradientColors);
        }

        [StaFact]
        public void A_renamed_environment_keeps_its_safety_protection()
        {
            // Safety keys on the environment name: renaming PRODUCTION used to drop the
            // type-the-server-name confirmation and the production checks without a word.
            var settings = Migrated();
            var (controls, _) = Build(settings);
            var environments = controls.Rules.Environments.Select(e => e.Clone()).ToList();
            environments[0].Name = "Live";
            controls.Rules.ApplyEnvironmentEdit(new EnvironmentEditResult(environments,
                new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { ["PRODUCTION"] = "Live" }, gradientColors: false));
            environments = controls.Rules.Environments.Select(e => e.Clone()).ToList();
            environments[0].Name = "Main";
            controls.Rules.ApplyEnvironmentEdit(new EnvironmentEditResult(environments,
                new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { ["Live"] = "Main" }, gradientColors: false));

            controls.Save(settings);

            Assert.Equal("Main", settings.Tabs.ColoringRules[0].Label);
            Assert.Equal(EnvironmentSafety.TypeServerName, settings.Safety.EnvironmentSeverity["Main"]);
            Assert.DoesNotContain(settings.Safety.EnvironmentSeverity.Keys, k => k == "PRODUCTION" || k == "Live");
            Assert.True(EnvironmentSafety.IsProduction(settings.Tabs.ColoringRules[0].Label, settings.Safety.EnvironmentSeverity));
        }

        [StaFact]
        public void The_grid_and_gradient_follow_Enable_tab_coloring()
        {
            var settings = Migrated();
            settings.Tabs.ColoringEnabled = false;
            var (controls, _) = Build(settings);

            Assert.False(controls.Rules.Root.IsEnabled);
            Assert.NotNull(controls.Rules.Root.ToolTip);

            settings.Tabs.ColoringEnabled = true;
            controls.Load(settings);
            Assert.True(controls.Rules.Root.IsEnabled);
            Assert.Null(controls.Rules.Root.ToolTip);
        }

        [StaFact]
        public void The_grid_renders_editable_cells_on_theme_brushes()
        {
            var (controls, _, panel) = Build(Migrated(), PageTheme.Dark);
            Layout(panel);
            var list = controls.Rules.List;

            var item = Assert.IsType<ListViewItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
            var boxes = VisualDescendants<TextBox>(item).ToList();
            Assert.Equal(2, boxes.Count);
            Assert.Equal("*PROD*,*LIVE*", boxes[0].Text);
            Assert.Same(PageTheme.Dark.Input, boxes[0].Background);
            var combo = Assert.Single(VisualDescendants<ComboBox>(item));
            Assert.Equal("PRODUCTION", combo.SelectedItem);

            // Editing a cell edits the row.
            boxes[0].Text = "*PRD*";
            combo.SelectedItem = "DEV";
            Assert.Equal("*PRD*", controls.Rules.Rows[0].Server);
            Assert.Equal("DEV", controls.Rules.Rows[0].Environment);

            // The column headers are painted from the page theme, not the stock light chrome.
            var header = VisualDescendants<GridViewColumnHeader>(list).First(h => "Environment".Equals(h.Content));
            var face = Assert.IsType<Border>(VisualTreeHelper.GetChild(header, 0));
            Assert.Same(PageTheme.Dark.Panel, face.Background);
        }

        [StaFact]
        public void A_rename_keeps_every_dropdown_on_its_environment()
        {
            var (controls, _, panel) = Build(Migrated(), PageTheme.Light);
            Layout(panel);
            var environments = controls.Rules.Environments.Select(e => e.Clone()).ToList();
            environments[2].Name = "Development";
            var renames = environments.Select((e, i) => (Old: controls.Rules.Environments[i].Name, New: e.Name))
                .ToDictionary(p => p.Old, p => p.New, System.StringComparer.OrdinalIgnoreCase);

            controls.Rules.ApplyEnvironmentEdit(new EnvironmentEditResult(environments, renames, gradientColors: false));
            Layout(panel);

            var selected = Enumerable.Range(0, 4)
                .Select(i => VisualDescendants<ComboBox>(controls.Rules.List.ItemContainerGenerator.ContainerFromIndex(i)).Single().SelectedItem)
                .ToList();
            Assert.Equal(new object[] { "PRODUCTION", "STAGING", "Development", "AZURE" }, selected);
            Assert.Equal(new[] { "PRODUCTION", "STAGING", "Development", "AZURE" }, controls.Rules.Rows.Select(r => r.Environment));
        }

        [StaFact]
        public void Restore_defaults_on_the_page_brings_back_the_default_rules_and_environments()
        {
            var settings = Migrated();
            settings.Tabs.ColoringRules.RemoveAt(0);
            settings.Tabs.Environments.Add(new TabEnvironment { Name = "QA", Color = "#9B59B6" });
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            dialog.ResetPageToDefaultsCore("Tabs & UI");

            var tabs = dialog.GetSettings().Tabs;
            Assert.Equal(new[] { "PRODUCTION", "STAGING", "DEV", "AZURE" }, tabs.Environments.Select(e => e.Name));
            Assert.Equal(4, tabs.ColoringRules.Count);
            Assert.All(tabs.ColoringRules, r => Assert.Equal(r.Label, r.Environment));
        }

        // ── Edit environments ───────────────────────────────────────────────────────────────

        private static readonly List<ColoringRule> RulesInUse = new()
        {
            new ColoringRule { Pattern = "*PROD*,*LIVE*", Environment = "PRODUCTION" },
            new ColoringRule { Pattern = "", DatabaseName = "QaDb", MatchTarget = EnvironmentMatcher.MatchTargetDatabase, Environment = "QA" },
        };

        private static EditEnvironmentsDialog Dialog()
        {
            var environments = TabEnvironment.CreateDefaults();
            environments.Add(new TabEnvironment { Name = "QA", Color = "#9B59B6" });
            return new EditEnvironmentsDialog(environments, RulesInUse, gradientColors: false);
        }

        [StaFact]
        public void The_dialog_is_titled_and_lists_the_environments()
        {
            var dialog = Dialog();

            Assert.Equal("AKML SQL – Edit environments", dialog.Title);
            Assert.Equal(new[] { "PRODUCTION", "STAGING", "DEV", "AZURE", "QA" }, dialog.Rows.Select(r => r.Name));
            Assert.Equal("#FF4444", dialog.Rows[0].Color);
            Assert.Equal(8, EditEnvironmentsDialog.PaletteColors.Count);
        }

        [StaFact]
        public void Deleting_an_environment_rules_use_is_refused_naming_the_rules()
        {
            var dialog = Dialog();

            Assert.False(dialog.TryDelete(dialog.Rows[0]));
            Assert.Contains("*PROD*,*LIVE*", dialog.StatusText);
            Assert.Equal(5, dialog.Rows.Count);

            Assert.True(dialog.TryDelete(dialog.Rows[1])); // STAGING: unused
            Assert.Equal(4, dialog.Rows.Count);
            Assert.Null(dialog.StatusText);
        }

        [StaFact]
        public void A_swatch_pick_sets_the_color_without_typing_it()
        {
            var dialog = Dialog();
            var (_, teal) = EditEnvironmentsDialog.PaletteColors.First(p => p.Name == "Teal");

            dialog.PickColor(dialog.Rows[2], teal);

            var result = dialog.TryBuildResult();
            Assert.NotNull(result);
            Assert.Equal(teal, result!.Environments[2].Color);
        }

        [StaFact]
        public void Save_validates_names_and_returns_the_renames()
        {
            var dialog = Dialog();
            dialog.Rows[1].Name = "dev"; // duplicates DEV

            Assert.Null(dialog.TryBuildResult());
            Assert.Contains("unique", dialog.StatusText);

            dialog.Rows[1].Name = "Staging";
            dialog.GradientColors = true;
            var result = dialog.TryBuildResult();

            Assert.NotNull(result);
            Assert.Equal("Staging", result!.Renames["STAGING"]);
            Assert.Equal("QA", result.Renames["qa"]);
            Assert.True(result.GradientColors);
        }

        [StaFact]
        public void A_new_environment_gets_a_free_name_and_an_unused_swatch()
        {
            var dialog = Dialog();

            var row = dialog.AddEnvironment();

            Assert.Equal("New environment", row.Name);
            Assert.Null(row.OriginalName);
            Assert.DoesNotContain(row.Color, dialog.Rows.Take(5).Select(r => r.Color));
            Assert.True(dialog.TryDelete(row));
        }

        [StaFact]
        public void Restore_default_environments_keeps_ones_rules_use()
        {
            var dialog = Dialog();
            dialog.Rows[0].Name = "Live";

            dialog.RestoreDefaults();

            Assert.Equal(new[] { "PRODUCTION", "STAGING", "DEV", "AZURE", "QA" }, dialog.Rows.Select(r => r.Name));
            Assert.Contains("'QA'", dialog.StatusText);
            var result = dialog.TryBuildResult();
            Assert.NotNull(result);
            Assert.Equal("PRODUCTION", result!.Renames["PRODUCTION"]);
        }
    }
}
