#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Design B (2026-10-13): Queries › History is two tabs — Recording (with Storage) and
    /// Restoring queries. The restore rows moved to their own section, still greyed by Enable
    /// SQL history on the first tab, and still save to the same settings.
    /// </summary>
    public class HistoryLayoutTests
    {
        [StaFact]
        public void History_has_recording_and_restoring_tabs()
        {
            var (dialog, _) = Build(new AppSettings());
            var page = Assert.IsAssignableFrom<TabbedPage>(Builders(dialog)["HistoryAll"]);

            Assert.Equal("Queries › History", page.Display);
            Assert.Equal(new[] { "Recording", "Restoring queries" }, page.Tabs.Select(t => t.Label));
            Assert.Equal(new[] { "History" }, page.Tabs[0].Sections.Select(s => s.Key));
            Assert.Equal(new[] { "HistoryRestore" }, page.Tabs[1].Sections.Select(s => s.Key));
        }

        [StaFact]
        public void Restore_settings_round_trip_from_the_second_tab()
        {
            var settings = new AppSettings();
            settings.Tabs.SessionRecovery = false;
            settings.Tabs.RestoreOnStartup = "never";
            settings.History.RestoreMaxQueries = 7;
            settings.History.ReconnectRestoredQueries = false;
            settings.Tabs.AutoSaveInterval = 90;
            settings.Tabs.MaxClosedTabs = 33;
            var (dialog, controls) = Build(settings);
            var restore = (HistoryRestoreControls)controls["HistoryRestore"];

            Assert.False(restore.RestoreOnStart.IsChecked);
            Assert.Equal(2, restore.WhenRestoring.SelectedIndex);
            Assert.Equal(7, (int)restore.RestoreMax.Value);
            Assert.False(restore.Reconnect.IsChecked);
            Assert.Equal("90", restore.AutoSave.Text);
            Assert.Equal(33, (int)restore.MaxClosedTabs.Value);

            restore.RestoreOnStart.IsChecked = true;
            restore.WhenRestoring.SelectedIndex = 0;
            restore.RestoreMax.Value = 12;
            restore.Reconnect.IsChecked = true;
            restore.AutoSave.Text = "120";
            restore.MaxClosedTabs.Value = 40;
            var saved = dialog.GetSettings();

            Assert.True(saved.Tabs.SessionRecovery);
            Assert.Equal("always", saved.Tabs.RestoreOnStartup);
            Assert.Equal(12, saved.History.RestoreMaxQueries);
            Assert.True(saved.History.ReconnectRestoredQueries);
            Assert.Equal(120, saved.Tabs.AutoSaveInterval);
            Assert.Equal(40, saved.Tabs.MaxClosedTabs);
        }

        [StaFact]
        public void Enable_SQL_history_still_greys_the_restore_rows_on_the_other_tab()
        {
            var (_, controls) = Build(new AppSettings());
            var recording = (HistoryControls)controls["History"];
            var restore = (HistoryRestoreControls)controls["HistoryRestore"];

            recording.Enabled.IsChecked = false;
            Assert.All(restore.Rows, row => Assert.False(row.IsEnabled));

            recording.Enabled.IsChecked = true;
            Assert.All(restore.Rows, row => Assert.True(row.IsEnabled));
        }

        private static (SettingsWindow Dialog, Dictionary<string, IPageControls> Controls) Build(AppSettings settings)
        {
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();
            var f = typeof(SettingsWindow).GetField("_pageControlsByKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (dialog, (Dictionary<string, IPageControls>)f.GetValue(dialog)!);
        }

        private static Dictionary<string, IPageBuilder> Builders(SettingsWindow dialog) =>
            (Dictionary<string, IPageBuilder>)typeof(SettingsWindow)
                .GetField("_pageBuilders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
    }
}
