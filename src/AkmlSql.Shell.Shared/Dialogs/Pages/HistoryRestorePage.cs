#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Queries › History › Restoring queries (the 2026-10 "rebalanced tree", design B): the
    /// restore-on-start rows that spec 040 (HIS-14, FR-047) put on the History page, now the
    /// page's second tab, in fitted rows. They save to the same <see cref="TabSettings"/> and
    /// <see cref="HistorySettings"/> fields and grey with Enable SQL history on the first tab.
    /// </summary>
    internal sealed class HistoryRestorePage : IPageBuilder
    {
        public string Key     => "HistoryRestore";
        public string Display => "Queries › History › Restoring queries";
        public string Title   => "Restoring queries";
        public string HelpTopic => "topics/options#queries-history";
        public string Help    => "How the queries that were open when SSMS closed are reopened on the next start: whether to reopen them, whether to ask, how many, whether they reconnect, how often unsaved text is kept, and how many closed tabs Ctrl+Shift+T can bring back.";

        /// <summary>
        /// The Enable SQL history switch on the Recording tab, handed over by
        /// <see cref="HistoryAllPage"/> before this section builds: every row here greys with it.
        /// </summary>
        internal CheckBox? MasterSwitch { get; set; }

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Restoring queries");

            var (rowRestore, chkRestore) = ctx.Rows.AddToggle(panel,
                "Restore open queries when SSMS starts", "Reopen the tabs from the last session", parent: MasterSwitch);
            ctx.Rows.Tip(rowRestore, "Reopen the query tabs that were open when SSMS last closed or crashed, and keep unsaved query text in SQL History.");
            ctx.RegisterSearch("Restore open queries when SSMS starts", "Reopen the query tabs that were open when SSMS last closed or crashed, and keep unsaved query text in SQL History.", "Toggle", rowRestore);

            var (left, right) = ctx.Rows.AddColumns(panel);

            var (rowWhen, cboWhen) = ctx.Rows.AddDropdown(left,
                "When restoring", new[] { "Always", "Ask", "Never" }, "", parent: MasterSwitch);
            ctx.Rows.Tip(rowWhen, "Reopen the queries at once, ask which to reopen, or don't reopen them.");
            ctx.RegisterSearch("When restoring", "Reopen the queries at once, ask which to reopen, or don't reopen them.", "Dropdown", rowWhen);

            var (rowRestoreMax, sldRestoreMax, lblRestoreMax) = ctx.Rows.AddSlider(right,
                "Maximum number of queries to restore", 1, 100, 20, "", parent: MasterSwitch);
            ctx.Rows.Tip(rowRestoreMax, "The newest queries are reopened first.");
            ctx.RegisterSearch("Maximum number of queries to restore", "The newest queries are reopened first.", "Slider", rowRestoreMax);

            var (rowReconnect, chkReconnect) = ctx.Rows.AddToggle(panel,
                "Automatically reconnect restored queries", "To the server and database each last ran on", parent: MasterSwitch);
            ctx.Rows.Tip(rowReconnect, "Connect each reopened query to the server and database it last ran on.");
            ctx.RegisterSearch("Automatically reconnect restored queries", "Connect each reopened query to the server and database it last ran on.", "Toggle", rowReconnect);

            var (saveLeft, saveRight) = ctx.Rows.AddColumns(panel);

            var (rowAutoSave, numAutoSave) = ctx.Rows.AddNumber(saveLeft,
                "Auto-save interval", 30, 300, 15, "seconds", "", MasterSwitch);
            ctx.Rows.Tip(rowAutoSave, "How often the text of open query tabs is saved to SQL History.");
            ctx.RegisterSearch("Auto-save interval", "How often the text of open query tabs is saved to SQL History.", "Number", rowAutoSave);

            var (rowMaxClosed, sldMaxClosed, lblMaxClosed) = ctx.Rows.AddSlider(saveRight,
                "Max closed tabs to remember", 1, 100, 20, "", parent: MasterSwitch);
            ctx.Rows.Tip(rowMaxClosed, "Number of recently closed tabs Ctrl+Shift+T can reopen. Older ones are reopened from SQL History.");
            ctx.RegisterSearch("Max closed tabs to remember", "Number of recently closed tabs Ctrl+Shift+T can reopen. Older ones are reopened from SQL History.", "Slider", rowMaxClosed);

            return new HistoryRestoreControls(chkRestore, cboWhen, sldRestoreMax, lblRestoreMax, chkReconnect, numAutoSave, sldMaxClosed, lblMaxClosed,
                new FrameworkElement[] { rowRestore, rowWhen, rowRestoreMax, rowReconnect, rowAutoSave, rowMaxClosed });
        }
    }

    internal sealed class HistoryRestoreControls : IPageControls
    {
        private readonly TextBlock _restoreMaxLabel;
        private readonly TextBlock _maxClosedLabel;

        public HistoryRestoreControls(CheckBox restoreOnStart, ComboBox whenRestoring, Slider restoreMax, TextBlock restoreMaxLabel,
            CheckBox reconnect, TextBox autoSave, Slider maxClosedTabs, TextBlock maxClosedLabel, IReadOnlyList<FrameworkElement> rows)
        {
            RestoreOnStart = restoreOnStart;
            WhenRestoring = whenRestoring;
            RestoreMax = restoreMax;
            _restoreMaxLabel = restoreMaxLabel;
            Reconnect = reconnect;
            AutoSave = autoSave;
            MaxClosedTabs = maxClosedTabs;
            _maxClosedLabel = maxClosedLabel;
            Rows = rows;
        }

        internal CheckBox RestoreOnStart { get; }
        internal ComboBox WhenRestoring { get; }
        internal Slider RestoreMax { get; }
        internal CheckBox Reconnect { get; }
        internal TextBox AutoSave { get; }
        internal Slider MaxClosedTabs { get; }

        /// <summary>The six rows, for the greying check (test seam).</summary>
        internal IReadOnlyList<FrameworkElement> Rows { get; }

        public void Load(AppSettings settings)
        {
            var h = settings.History;
            var t = settings.Tabs;
            RestoreOnStart.IsChecked = t.SessionRecovery;
            WhenRestoring.SelectedIndex = (t.RestoreOnStartup ?? string.Empty).ToLowerInvariant() switch
            {
                "always" => 0,
                "never" => 2,
                _ => 1,
            };
            SetSlider(RestoreMax, _restoreMaxLabel, h.RestoreMaxQueries);
            Reconnect.IsChecked = h.ReconnectRestoredQueries;
            RowFactory.SetNumber(AutoSave, t.AutoSaveInterval);
            SetSlider(MaxClosedTabs, _maxClosedLabel, t.MaxClosedTabs);
        }

        public void Save(AppSettings settings)
        {
            settings.Tabs.SessionRecovery = RestoreOnStart.IsChecked == true;
            settings.Tabs.RestoreOnStartup = WhenRestoring.SelectedIndex switch
            {
                0 => "always",
                2 => "never",
                _ => "prompt",
            };
            settings.History.RestoreMaxQueries = (int)RestoreMax.Value;
            settings.History.ReconnectRestoredQueries = Reconnect.IsChecked == true;
            settings.Tabs.AutoSaveInterval = RowFactory.GetNumber(AutoSave);
            settings.Tabs.MaxClosedTabs = (int)MaxClosedTabs.Value;
        }

        private static void SetSlider(Slider slider, TextBlock label, int value)
        {
            slider.Value = value;
            label.Text = ((int)slider.Value).ToString(CultureInfo.InvariantCulture);
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
