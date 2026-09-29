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
        public string Title   => "SQL History";
        public string Help    => "Controls recording of executed SQL statements to a local history database, how long that history is kept, and how the queries open when SSMS closed are restored on the next start.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Recording");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable SQL history recording",
                "Record all executed SQL statements to a local database. Takes effect after SSMS restarts.");
            ctx.RegisterSearch("Enable SQL history recording", "Record all executed SQL statements to a local database. Takes effect after SSMS restarts.", "Toggle", rowEnabled);

            // Spec 040 (HIS-06): "Record failed executions" and "Encrypt at rest" changed nothing and
            // are hidden; their saved values are kept.

            var (rowDedup, chkDedup) = ctx.Rows.AddToggle(panel,
                "Group repeated runs of the same query",
                "Show one row per query tab, with its runs and versions inside.");
            ctx.RegisterSearch("Group repeated runs of the same query", "Show one row per query tab, with its runs and versions inside.", "Toggle", rowDedup);

            // Spec 040 (HIS-13): longer queries are kept up to this size, with a note at the end.
            var (rowQuerySize, sldQuerySize, lblQuerySize) = ctx.Rows.AddSlider(panel,
                "Maximum query size", 16, 1024, 1024,
                "Largest query text kept, in KB. Longer queries are cut, with a note at the end.");
            ctx.RegisterSearch("Maximum query size", "Largest query text kept, in KB. Longer queries are cut, with a note at the end.", "Slider", rowQuerySize);

            var (rowRemember, chkRemember) = ctx.Rows.AddToggle(panel,
                "Remember advanced search settings",
                "Keep the Advanced search filters of the SQL History window between sessions.");
            ctx.RegisterSearch("Remember advanced search settings", "Keep the Advanced search filters of the SQL History window between sessions.", "Toggle", rowRemember);

            // Spec 040 (HIS-14, FR-047): restore on start lives here, in SQL Prompt's words.
            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Restoring queries");

            var (rowRestore, chkRestore) = ctx.Rows.AddToggle(panel,
                "Restore open queries when SSMS starts",
                "Reopen the query tabs that were open when SSMS last closed or crashed, and keep unsaved query text in SQL History.");
            ctx.RegisterSearch("Restore open queries when SSMS starts", "Reopen the query tabs that were open when SSMS last closed or crashed, and keep unsaved query text in SQL History.", "Toggle", rowRestore);

            var (rowWhen, cboWhen) = ctx.Rows.AddDropdown(panel,
                "When restoring",
                new[] { "Always", "Ask", "Never" },
                "Reopen the queries at once, ask which to reopen, or don't reopen them.");
            ctx.RegisterSearch("When restoring", "Reopen the queries at once, ask which to reopen, or don't reopen them.", "Dropdown", rowWhen);

            var (rowRestoreMax, sldRestoreMax, lblRestoreMax) = ctx.Rows.AddSlider(panel,
                "Maximum number of queries to restore", 1, 100, 20,
                "The newest queries are reopened first.");
            ctx.RegisterSearch("Maximum number of queries to restore", "The newest queries are reopened first.", "Slider", rowRestoreMax);

            var (rowReconnect, chkReconnect) = ctx.Rows.AddToggle(panel,
                "Automatically reconnect restored queries",
                "Connect each reopened query to the server and database it last ran on.");
            ctx.RegisterSearch("Automatically reconnect restored queries", "Connect each reopened query to the server and database it last ran on.", "Toggle", rowReconnect);

            var (rowAutoSave, sldAutoSave, lblAutoSave) = ctx.Rows.AddSlider(panel,
                "Auto-save interval (seconds)", 30, 300, 60,
                "How often the text of open query tabs is saved to SQL History.");
            ctx.RegisterSearch("Auto-save interval (seconds)", "How often the text of open query tabs is saved to SQL History.", "Slider", rowAutoSave);

            var (rowMaxClosed, sldMaxClosed, lblMaxClosed) = ctx.Rows.AddSlider(panel,
                "Max closed tabs to remember", 1, 100, 20,
                "Number of recently closed tabs Ctrl+Shift+T can reopen. Older ones are reopened from SQL History.");
            ctx.RegisterSearch("Max closed tabs to remember", "Number of recently closed tabs Ctrl+Shift+T can reopen. Older ones are reopened from SQL History.", "Slider", rowMaxClosed);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Storage");

            var (rowRetention, sldRetention, lblRetention) = ctx.Rows.AddSlider(panel,
                "Retention (days)", 1, 3650, 90,
                "Number of days to keep history entries before pruning. Takes effect after SSMS restarts.");
            ctx.RegisterSearch("Retention (days)", "Number of days to keep history entries before pruning. Takes effect after SSMS restarts.", "Slider", rowRetention);

            var (rowMax, sldMax, lblMax) = ctx.Rows.AddSlider(panel,
                "Max entries", 1000, 1_000_000, 100_000,
                "Maximum number of history entries stored. Takes effect after SSMS restarts.", largeRange: true);
            ctx.RegisterSearch("Max entries", "Maximum number of history entries stored. Takes effect after SSMS restarts.", "Slider", rowMax);

            var (rowDisableTrim, chkDisableTrim) = ctx.Rows.AddToggle(panel,
                "Disable automatic history trimming",
                "Keep all history entries and version snapshots — never purge based on retention days or max entries. Takes effect after SSMS restarts.");
            ctx.RegisterSearch("Disable automatic history trimming", "Keep all history entries and version snapshots — never purge based on retention days or max entries. Takes effect after SSMS restarts.", "Toggle", rowDisableTrim);

            return new HistoryControls(chkEnabled, chkDedup, sldRetention, lblRetention, sldMax, lblMax, chkDisableTrim)
            {
                QuerySize = (sldQuerySize, lblQuerySize),
                RememberAdvancedSearch = chkRemember,
                RestoreOnStart = chkRestore,
                WhenRestoring = cboWhen,
                RestoreMax = (sldRestoreMax, lblRestoreMax),
                Reconnect = chkReconnect,
                AutoSave = (sldAutoSave, lblAutoSave),
                MaxClosedTabs = (sldMaxClosed, lblMaxClosed),
            };
        }
    }

    internal sealed class HistoryControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _deduplication;
        private readonly Slider _retentionDays;
        private readonly TextBlock _retentionLabel;
        private readonly Slider _maxEntries;
        private readonly TextBlock _maxEntriesLabel;
        private readonly CheckBox _disableAutoTrim;

        public HistoryControls(CheckBox enabled, CheckBox dedup,
            Slider retention, TextBlock retentionLbl, Slider maxEntries, TextBlock maxEntriesLbl,
            CheckBox disableAutoTrim)
        {
            _enabled = enabled;
            _deduplication = dedup;
            _retentionDays = retention;
            _retentionLabel = retentionLbl;
            _maxEntries = maxEntries;
            _maxEntriesLabel = maxEntriesLbl;
            _disableAutoTrim = disableAutoTrim;
        }

        // Spec 040 (HIS-13/HIS-14) rows.
        internal (Slider Slider, TextBlock Label) QuerySize { get; set; }
        internal CheckBox? RememberAdvancedSearch { get; set; }
        internal CheckBox? RestoreOnStart { get; set; }
        internal ComboBox? WhenRestoring { get; set; }
        internal (Slider Slider, TextBlock Label) RestoreMax { get; set; }
        internal CheckBox? Reconnect { get; set; }
        internal (Slider Slider, TextBlock Label) AutoSave { get; set; }
        internal (Slider Slider, TextBlock Label) MaxClosedTabs { get; set; }

        public void Load(AppSettings settings)
        {
            var h = settings.History;
            _enabled.IsChecked = h.Enabled;
            _deduplication.IsChecked = h.Deduplication;
            _disableAutoTrim.IsChecked = h.DisableAutoTrim;
            _retentionDays.Value = h.RetentionDays;
            _retentionLabel.Text = h.RetentionDays.ToString(CultureInfo.InvariantCulture);
            _maxEntries.Value = h.MaxEntries;
            _maxEntriesLabel.Text = h.MaxEntries.ToString(CultureInfo.InvariantCulture);

            var t = settings.Tabs;
            SetSlider(QuerySize, h.MaxQuerySizeKb);
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
            SetSlider(AutoSave, t.AutoSaveInterval);
            SetSlider(MaxClosedTabs, t.MaxClosedTabs);
        }

        public void Save(AppSettings settings)
        {
            settings.History.Enabled = _enabled.IsChecked == true;
            settings.History.Deduplication = _deduplication.IsChecked == true;
            settings.History.DisableAutoTrim = _disableAutoTrim.IsChecked == true;
            settings.History.RetentionDays = (int)_retentionDays.Value;
            settings.History.MaxEntries = (int)_maxEntries.Value;

            if (QuerySize.Slider != null) settings.History.MaxQuerySizeKb = (int)QuerySize.Slider.Value;
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
            if (AutoSave.Slider != null) settings.Tabs.AutoSaveInterval = (int)AutoSave.Slider.Value;
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
