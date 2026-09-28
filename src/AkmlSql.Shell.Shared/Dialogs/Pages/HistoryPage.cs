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
        public string Help    => "Controls recording of executed SQL statements to a local history database, and how long that history is kept — retention period, maximum entries and automatic trimming.";

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

            return new HistoryControls(chkEnabled, chkDedup, sldRetention, lblRetention, sldMax, lblMax, chkDisableTrim);
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
        }

        public void Save(AppSettings settings)
        {
            settings.History.Enabled = _enabled.IsChecked == true;
            settings.History.Deduplication = _deduplication.IsChecked == true;
            settings.History.DisableAutoTrim = _disableAutoTrim.IsChecked == true;
            settings.History.RetentionDays = (int)_retentionDays.Value;
            settings.History.MaxEntries = (int)_maxEntries.Value;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
