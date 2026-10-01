#nullable enable
using System.Globalization;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class HistoryPage : IPageBuilder
    {
        public string Key     => "History";
        public string Display => "Queries › History";
        public string Title   => "History";
        public string HelpTopic => "topics/options#queries-history";
        public string Help    => "Controls recording of executed SQL statements to a local history database, how long that history is kept, and how the queries open when SSMS closed are restored on the next start.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Recording");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable SQL history",
                "Record all executed SQL statements to a local database. Takes effect after SSMS restarts.");
            ctx.RegisterSearch("Enable SQL history", "Record all executed SQL statements to a local database. Takes effect after SSMS restarts.", "Toggle", rowEnabled);

            // Spec 040 (HIS-06): "Record failed executions" and "Encrypt at rest" changed nothing and
            // are hidden; their saved values are kept.

            var (rowDedup, chkDedup) = ctx.Rows.AddToggle(panel,
                "Group repeated runs of the same query",
                "Show one row per query tab, with its runs and versions inside.", parent: chkEnabled);
            ctx.RegisterSearch("Group repeated runs of the same query", "Show one row per query tab, with its runs and versions inside.", "Toggle", rowDedup);

            // Spec 040 (HIS-13): longer queries are kept up to this size, with a note at the end.
            var (rowQuerySize, numQuerySize) = ctx.Rows.AddNumber(panel,
                "Maximum query size", 16, 1024, 16, "KB",
                "Largest query text kept. Longer queries are cut, with a note at the end.", chkEnabled);
            ctx.RegisterSearch("Maximum query size", "Largest query text kept. Longer queries are cut, with a note at the end.", "Number", rowQuerySize);

            var (rowRemember, chkRemember) = ctx.Rows.AddToggle(panel,
                "Remember advanced search settings",
                "Keep the Advanced search filters of the SQL History window between sessions.", parent: chkEnabled);
            ctx.RegisterSearch("Remember advanced search settings", "Keep the Advanced search filters of the SQL History window between sessions.", "Toggle", rowRemember);

            // Spec 040 (HIS-14, FR-047): restore on start lives here, in SQL Prompt's words.
            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Restoring queries");

            var (rowRestore, chkRestore) = ctx.Rows.AddToggle(panel,
                "Restore open queries when SSMS starts",
                "Reopen the query tabs that were open when SSMS last closed or crashed, and keep unsaved query text in SQL History.", parent: chkEnabled);
            ctx.RegisterSearch("Restore open queries when SSMS starts", "Reopen the query tabs that were open when SSMS last closed or crashed, and keep unsaved query text in SQL History.", "Toggle", rowRestore);

            var (rowWhen, cboWhen) = ctx.Rows.AddDropdown(panel,
                "When restoring",
                new[] { "Always", "Ask", "Never" },
                "Reopen the queries at once, ask which to reopen, or don't reopen them.", parent: chkEnabled);
            ctx.RegisterSearch("When restoring", "Reopen the queries at once, ask which to reopen, or don't reopen them.", "Dropdown", rowWhen);

            var (rowRestoreMax, sldRestoreMax, lblRestoreMax) = ctx.Rows.AddSlider(panel,
                "Maximum number of queries to restore", 1, 100, 20,
                "The newest queries are reopened first.", parent: chkEnabled);
            ctx.RegisterSearch("Maximum number of queries to restore", "The newest queries are reopened first.", "Slider", rowRestoreMax);

            var (rowReconnect, chkReconnect) = ctx.Rows.AddToggle(panel,
                "Automatically reconnect restored queries",
                "Connect each reopened query to the server and database it last ran on.", parent: chkEnabled);
            ctx.RegisterSearch("Automatically reconnect restored queries", "Connect each reopened query to the server and database it last ran on.", "Toggle", rowReconnect);

            var (rowAutoSave, numAutoSave) = ctx.Rows.AddNumber(panel,
                "Auto-save interval", 30, 300, 15, "seconds",
                "How often the text of open query tabs is saved to SQL History.", chkEnabled);
            ctx.RegisterSearch("Auto-save interval", "How often the text of open query tabs is saved to SQL History.", "Number", rowAutoSave);

            var (rowMaxClosed, sldMaxClosed, lblMaxClosed) = ctx.Rows.AddSlider(panel,
                "Max closed tabs to remember", 1, 100, 20,
                "Number of recently closed tabs Ctrl+Shift+T can reopen. Older ones are reopened from SQL History.", parent: chkEnabled);
            ctx.RegisterSearch("Max closed tabs to remember", "Number of recently closed tabs Ctrl+Shift+T can reopen. Older ones are reopened from SQL History.", "Slider", rowMaxClosed);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Storage");

            var (rowRetention, numRetention) = ctx.Rows.AddNumber(panel,
                "Retention", 1, 3650, 1, "days",
                "Number of days to keep history entries before pruning. Takes effect after SSMS restarts.", chkEnabled);
            ctx.RegisterSearch("Retention", "Number of days to keep history entries before pruning. Takes effect after SSMS restarts.", "Number", rowRetention);

            var (rowMax, numMax) = ctx.Rows.AddNumber(panel,
                "Max entries", 1000, 1_000_000, 1000, "entries",
                "Maximum number of history entries stored. Takes effect after SSMS restarts.", chkEnabled);
            ctx.RegisterSearch("Max entries", "Maximum number of history entries stored. Takes effect after SSMS restarts.", "Number", rowMax);

            var (rowDisableTrim, chkDisableTrim) = ctx.Rows.AddToggle(panel,
                "Disable automatic history trimming",
                "Keep all history entries and version snapshots — never purge based on retention days or max entries. Takes effect after SSMS restarts.", parent: chkEnabled);
            ctx.RegisterSearch("Disable automatic history trimming", "Keep all history entries and version snapshots — never purge based on retention days or max entries. Takes effect after SSMS restarts.", "Toggle", rowDisableTrim);

            return new HistoryControls(chkEnabled, chkDedup, numRetention, numMax, chkDisableTrim)
            {
                QuerySize = numQuerySize,
                RememberAdvancedSearch = chkRemember,
                RestoreOnStart = chkRestore,
                WhenRestoring = cboWhen,
                RestoreMax = (sldRestoreMax, lblRestoreMax),
                Reconnect = chkReconnect,
                AutoSave = numAutoSave,
                MaxClosedTabs = (sldMaxClosed, lblMaxClosed),
            };
        }
    }

    internal sealed class HistoryControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _deduplication;
        private readonly TextBox _retentionDays;
        private readonly TextBox _maxEntries;
        private readonly CheckBox _disableAutoTrim;

        public HistoryControls(CheckBox enabled, CheckBox dedup, TextBox retention, TextBox maxEntries, CheckBox disableAutoTrim)
        {
            _enabled = enabled;
            _deduplication = dedup;
            _retentionDays = retention;
            _maxEntries = maxEntries;
            _disableAutoTrim = disableAutoTrim;
        }

        // Spec 040 (HIS-13/HIS-14) rows.
        internal TextBox? QuerySize { get; set; }
        internal CheckBox? RememberAdvancedSearch { get; set; }
        internal CheckBox? RestoreOnStart { get; set; }
        internal ComboBox? WhenRestoring { get; set; }
        internal (Slider Slider, TextBlock Label) RestoreMax { get; set; }
        internal CheckBox? Reconnect { get; set; }
        internal TextBox? AutoSave { get; set; }
        internal (Slider Slider, TextBlock Label) MaxClosedTabs { get; set; }

        public void Load(AppSettings settings)
        {
            var h = settings.History;
            _enabled.IsChecked = h.Enabled;
            _deduplication.IsChecked = h.Deduplication;
            _disableAutoTrim.IsChecked = h.DisableAutoTrim;
            RowFactory.SetNumber(_retentionDays, h.RetentionDays);
            RowFactory.SetNumber(_maxEntries, h.MaxEntries);

            var t = settings.Tabs;
            if (QuerySize != null) RowFactory.SetNumber(QuerySize, h.MaxQuerySizeKb);
            if (RememberAdvancedSearch != null) RememberAdvancedSearch.IsChecked = h.RememberAdvancedSearch;
            if (RestoreOnStart != null) RestoreOnStart.IsChecked = t.SessionRecovery;
            if (WhenRestoring != null)
                WhenRestoring.SelectedIndex = (t.RestoreOnStartup ?? string.Empty).ToLowerInvariant() switch
                {
                    "always" => 0,
                    "never" => 2,
                    _ => 1,
                };
            SetSlider(RestoreMax, h.RestoreMaxQueries);
            if (Reconnect != null) Reconnect.IsChecked = h.ReconnectRestoredQueries;
            if (AutoSave != null) RowFactory.SetNumber(AutoSave, t.AutoSaveInterval);
            SetSlider(MaxClosedTabs, t.MaxClosedTabs);
        }

        public void Save(AppSettings settings)
        {
            settings.History.Enabled = _enabled.IsChecked == true;
            settings.History.Deduplication = _deduplication.IsChecked == true;
            settings.History.DisableAutoTrim = _disableAutoTrim.IsChecked == true;
            settings.History.RetentionDays = RowFactory.GetNumber(_retentionDays);
            settings.History.MaxEntries = RowFactory.GetNumber(_maxEntries);

            if (QuerySize != null) settings.History.MaxQuerySizeKb = RowFactory.GetNumber(QuerySize);
            if (RememberAdvancedSearch != null) settings.History.RememberAdvancedSearch = RememberAdvancedSearch.IsChecked == true;
            if (RestoreOnStart != null) settings.Tabs.SessionRecovery = RestoreOnStart.IsChecked == true;
            if (WhenRestoring != null)
                settings.Tabs.RestoreOnStartup = WhenRestoring.SelectedIndex switch
                {
                    0 => "always",
                    2 => "never",
                    _ => "prompt",
                };
            if (RestoreMax.Slider != null) settings.History.RestoreMaxQueries = (int)RestoreMax.Slider.Value;
            if (Reconnect != null) settings.History.ReconnectRestoredQueries = Reconnect.IsChecked == true;
            if (AutoSave != null) settings.Tabs.AutoSaveInterval = RowFactory.GetNumber(AutoSave);
            if (MaxClosedTabs.Slider != null) settings.Tabs.MaxClosedTabs = (int)MaxClosedTabs.Slider.Value;
        }

        private static void SetSlider((Slider Slider, TextBlock Label) row, int value)
        {
            if (row.Slider == null) return;
            row.Slider.Value = value;
            row.Label.Text = ((int)row.Slider.Value).ToString(CultureInfo.InvariantCulture);
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
