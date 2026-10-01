#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AkmlSql.Core;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Analysis;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T153, OPT-09, FR-055) — the Code analysis rules window, ported from WinForms to a
    /// themed WPF window. It must return the same overrides as the old dialog for the same edits
    /// (only changed rows; a row put back to its default removes the override), report which
    /// session suppressions Save restores, and group the rules by category. The Options page's
    /// "Manage rules…" path takes the overrides the window saved from disk.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class ManageRulesWindowTests : AppDataIsolatedTest
    {
        public ManageRulesWindowTests() : base("akmlsql-manage-rules-test-") { }

        private static AnalysisRuleInfoDto Rule(string id, string category, int defaultSeverity, int effectiveSeverity,
            bool enabled, bool autoFix = false) => new AnalysisRuleInfoDto
        {
            RuleId = id,
            Name = id + " name",
            Category = category,
            DefaultSeverity = defaultSeverity,
            EffectiveSeverity = effectiveSeverity,
            Enabled = enabled,
            AutoFixable = autoFix,
            Description = id + " checks something.",
        };

        /// <summary>The rules as the engine reports them, given <see cref="SeedConfig"/>'s overrides.</summary>
        private static AnalysisRuleInfoDto[] Catalog() => new[]
        {
            Rule("ST001", "Style", defaultSeverity: 1, effectiveSeverity: 3, enabled: true),     // saved: error
            Rule("PE002", "Performance", 2, 2, true, autoFix: true),
            Rule("PE001", "Performance", 2, 2, true),
            Rule("BP001", "BestPractices", 2, 2, false),                                         // saved: off
            Rule("BP002", "BestPractices", 2, 2, false),                                         // saved: off
            Rule("SE001", "Security", 3, 3, true),
            Rule("DE001", "Design", 2, 1, true),                                                 // .casettings: information
            Rule("DE002", "Design", 2, 1, true),                                                 // .casettings: information
        };

        private static void SeedConfig()
        {
            var settings = new AppSettings();
            settings.CodeAnalysis.RuleOverrides = new Dictionary<string, RuleOverride>
            {
                ["ST001"] = new RuleOverride { Enabled = true, Severity = "error" },
                ["BP001"] = new RuleOverride { Enabled = false, Severity = "warning" },
                ["BP002"] = new RuleOverride { Enabled = false, Severity = "warning" },
                ["XX999"] = new RuleOverride { Enabled = false, Severity = string.Empty }, // not in the catalog
            };
            ConfigManager.Save(settings);
        }

        private static ManageRulesDialog.RuleRow RowFor(ManageRulesDialog window, string ruleId) =>
            window.Rows.Single(r => r.Dto.RuleId == ruleId);

        // ─── The old WinForms dialog's GetOverrides, verbatim, over (dto, Enabled cell, Severity cell) ───

        private static readonly string[] OldSeverityLabels = { "Hint", "Information", "Warning", "Error" };

        private static Dictionary<string, RuleOverride> OldDialogOverrides(
            IEnumerable<(AnalysisRuleInfoDto Dto, bool Enabled, string SeverityLabel)> gridRows)
        {
            var existing = ConfigManager.Load().CodeAnalysis.RuleOverrides;
            var result = new Dictionary<string, RuleOverride>(existing, StringComparer.OrdinalIgnoreCase);
            foreach (var (dto, enabled, label) in gridRows)
            {
                int idx = Array.IndexOf(OldSeverityLabels, label);
                int sevIdx = idx >= 0 ? idx : 2;
                bool unchanged = enabled == dto.Enabled && sevIdx == dto.EffectiveSeverity;
                if (unchanged) continue;
                if (enabled && sevIdx == dto.DefaultSeverity)
                {
                    result.Remove(dto.RuleId);
                }
                else
                {
                    result[dto.RuleId] = new RuleOverride
                    {
                        Enabled = enabled,
                        Severity = sevIdx switch { 0 => "hint", 1 => "information", 2 => "warning", 3 => "error", _ => "warning" },
                    };
                }
            }
            return result;
        }

        private static void AssertSameOverrides(IDictionary<string, RuleOverride> expected, IDictionary<string, RuleOverride> actual)
        {
            Assert.Equal(
                expected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
                actual.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            foreach (var key in expected.Keys)
            {
                Assert.Equal(expected[key].Enabled, actual[key].Enabled);
                Assert.Equal(expected[key].Severity, actual[key].Severity);
            }
        }

        [StaFact]
        public void Edits_give_the_same_overrides_as_the_old_dialog()
        {
            SeedConfig();
            var catalog = Catalog();
            var window = new ManageRulesDialog(catalog);

            RowFor(window, "PE001").EnabledBox.IsChecked = false;      // off                 → override
            RowFor(window, "PE002").SeverityBox.SelectedItem = "Error"; // warning → error     → override
            RowFor(window, "ST001").SeverityBox.SelectedItem = "Information"; // back to default → removed
            RowFor(window, "BP002").EnabledBox.IsChecked = true;       // back on (default)   → removed
            RowFor(window, "SE001").EnabledBox.IsChecked = false;      // off and on again    → untouched
            RowFor(window, "SE001").EnabledBox.IsChecked = true;
            RowFor(window, "DE002").EnabledBox.IsChecked = false;      // off at .casettings' severity → override

            var actual = window.GetOverrides();

            // The old dialog, fed the same rules and the same cell values.
            var oldGrid = catalog.Select(dto =>
            {
                var row = RowFor(window, dto.RuleId);
                return (dto, row.EnabledBox.IsChecked == true, row.SeverityBox.SelectedItem as string ?? string.Empty);
            });
            AssertSameOverrides(OldDialogOverrides(oldGrid), actual);

            // And spelled out: only changed rows are written, a row back at its default drops its
            // override, untouched saved overrides (and ones for rules not listed) survive, and a
            // .casettings severity is never baked in.
            AssertSameOverrides(new Dictionary<string, RuleOverride>
            {
                ["BP001"] = new RuleOverride { Enabled = false, Severity = "warning" },
                ["XX999"] = new RuleOverride { Enabled = false, Severity = string.Empty },
                ["PE001"] = new RuleOverride { Enabled = false, Severity = "warning" },
                ["PE002"] = new RuleOverride { Enabled = true, Severity = "error" },
                ["DE002"] = new RuleOverride { Enabled = false, Severity = "information" },
            }, actual);
            Assert.False(actual.ContainsKey("DE001"));
            Assert.False(actual.ContainsKey("SE001"));
            window.Close();
        }

        [StaFact]
        public void Without_edits_the_saved_overrides_come_back_unchanged()
        {
            SeedConfig();
            var window = new ManageRulesDialog(Catalog());

            AssertSameOverrides(ConfigManager.Load().CodeAnalysis.RuleOverrides, window.GetOverrides());
            window.Close();
        }

        [StaFact]
        public void Restore_lists_the_session_suppressed_rules_it_restores()
        {
            SeedConfig();
            var window = new ManageRulesDialog(Catalog(), new[] { "ST001", "pe001" });

            Assert.NotNull(window.SessionRestoreButton);
            Assert.True(RowFor(window, "PE001").IsSessionSuppressed);
            Assert.False(RowFor(window, "PE002").IsSessionSuppressed);
            Assert.False(window.RestoreSessionSuppressions);
            Assert.Empty(window.RestoredSessionRules);

            window.SessionRestoreButton!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.True(window.RestoreSessionSuppressions);
            Assert.Equal(new[] { "pe001", "ST001" }, window.RestoredSessionRules);
            Assert.False(window.SessionRestoreButton.IsEnabled);

            // Restoring is not a rule edit: the overrides are untouched.
            AssertSameOverrides(ConfigManager.Load().CodeAnalysis.RuleOverrides, window.GetOverrides());
            window.Close();
        }

        [StaFact]
        public void Without_session_suppressions_there_is_no_restore_strip()
        {
            var window = new ManageRulesDialog(Catalog());

            Assert.Null(window.SessionRestoreButton);
            Assert.False(window.RestoreSessionSuppressions);
            Assert.Empty(window.RestoredSessionRules);
            window.Close();
        }

        [StaFact]
        public void Rules_are_grouped_by_category_under_expandable_headers()
        {
            var window = new ManageRulesDialog(Catalog());

            Assert.Equal(new[] { "BestPractices", "Design", "Performance", "Security", "Style" },
                window.Groups.Select(g => g.Category));
            Assert.All(window.Groups, g => Assert.All(g.Rows, r => Assert.Equal(g.Category, r.Dto.Category)));
            Assert.Equal(new[] { "PE001", "PE002" }, window.Groups.Single(g => g.Category == "Performance").Rows.Select(r => r.Dto.RuleId));

            // Every rule exactly once.
            Assert.Equal(Catalog().Select(r => r.RuleId).OrderBy(i => i), window.Rows.Select(r => r.Dto.RuleId).OrderBy(i => i));

            foreach (var group in window.Groups)
            {
                Assert.True(group.Expander.IsExpanded);
                var headerTexts = LogicalTree.Descendants<TextBlock>((DependencyObject)group.Expander.Header).Select(t => t.Text);
                Assert.Contains(group.Category, headerTexts);
            }

            // The header counts the rules that are on, and follows the check boxes.
            var bestPractices = window.Groups.Single(g => g.Category == "BestPractices");
            Assert.Equal("0 of 2 on", bestPractices.CountText);
            RowFor(window, "BP002").EnabledBox.IsChecked = true;
            Assert.Equal("1 of 2 on", bestPractices.CountText);
            window.Close();
        }

        [StaFact]
        public void Each_row_has_enabled_severity_and_fix()
        {
            var window = new ManageRulesDialog(Catalog());

            var st001 = RowFor(window, "ST001");
            Assert.True(st001.EnabledBox.IsChecked);
            Assert.Equal(new[] { "Hint", "Information", "Warning", "Error" }, st001.SeverityBox.Items.Cast<string>());
            Assert.Equal("Error", st001.SeverityBox.SelectedItem);            // the effective severity
            Assert.False(RowFor(window, "BP001").EnabledBox.IsChecked);

            static IEnumerable<string> Texts(ManageRulesDialog.RuleRow row) =>
                LogicalTree.Descendants<TextBlock>(row.Container).Select(t => t.Text);
            Assert.Contains("✓", Texts(RowFor(window, "PE002")));
            Assert.DoesNotContain("✓", Texts(RowFor(window, "PE001")));
            Assert.Contains("PE001", Texts(RowFor(window, "PE001")));
            Assert.Contains("PE001 name", Texts(RowFor(window, "PE001")));
            window.Close();
        }

        [StaFact]
        public void Window_is_themed_titled_and_shows_the_settings_file_and_selected_rule()
        {
            var window = new ManageRulesDialog(Catalog());

            Assert.IsAssignableFrom<ThemeAwareWindow>(window);
            Assert.Equal("AKML SQL – Code analysis rules", window.Title);
            Assert.Equal(Constants.ConfigFilePath, window.SettingsFileText);
            Assert.Contains(LogicalTree.Descendants<TextBlock>(window), t => t.Text == "Settings file:");
            Assert.True(window.CancelButton.IsCancel);
            Assert.Equal("Save", window.SaveButton.Content);

            Assert.Null(window.SelectedRow);
            window.Select(RowFor(window, "PE002"));
            Assert.Same(RowFor(window, "PE002"), window.SelectedRow);
            Assert.Contains("PE002", window.DetailTitleText);
            Assert.Equal("PE002 checks something.", window.DetailText);
            window.Close();
        }

        [StaFact]
        public void Options_page_saves_the_overrides_the_rules_window_left_on_disk()
        {
            // Options loaded its working copy before "Manage rules…" opened.
            var working = new AppSettings();
            working.CodeAnalysis.RuleOverrides = new Dictionary<string, RuleOverride>
            {
                ["PE001"] = new RuleOverride { Enabled = false, Severity = "warning" },
            };
            var controls = new CodeAnalysisControls(new CheckBox(), new CheckBox(), new CheckBox());
            controls.Load(working);

            // Until the window has been used, the page leaves the overrides alone.
            controls.Save(working);
            Assert.Equal(new[] { "PE001" }, working.CodeAnalysis.RuleOverrides.Keys);

            // The window saves new overrides; the page takes them from disk.
            var disk = new AppSettings();
            disk.CodeAnalysis.RuleOverrides = new Dictionary<string, RuleOverride>
            {
                ["ST001"] = new RuleOverride { Enabled = false, Severity = "information" },
            };
            ConfigManager.Save(disk);
            controls.RefreshRuleOverridesFromDisk();

            controls.Save(working);
            Assert.Equal(new[] { "ST001" }, working.CodeAnalysis.RuleOverrides.Keys);
            Assert.False(working.CodeAnalysis.RuleOverrides["ST001"].Enabled);

            // Resetting the page keeps them.
            var afterReset = new AppSettings();
            controls.Reset(new AppSettings());
            controls.Save(afterReset);
            Assert.Equal(new[] { "ST001" }, afterReset.CodeAnalysis.RuleOverrides.Keys);

            // Import / Restore all defaults replace the working copy, and win.
            var imported = new AppSettings();
            imported.CodeAnalysis.RuleOverrides = new Dictionary<string, RuleOverride>
            {
                ["BP001"] = new RuleOverride { Enabled = false, Severity = "warning" },
            };
            controls.Load(imported);
            controls.Save(imported);
            Assert.Equal(new[] { "BP001" }, imported.CodeAnalysis.RuleOverrides.Keys);
        }
    }
}
