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

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Recording");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable SQL history recording",
                "Record all executed SQL statements to a local database");
            ctx.RegisterSearch("Enable SQL history recording", "Record all executed SQL statements to a local database", "Toggle", rowEnabled);

            var (rowFailures, chkFailures) = ctx.Rows.AddToggle(panel,
                "Record failed executions",
                "Also record statements that resulted in errors");
            ctx.RegisterSearch("Record failed executions", "Also record statements that resulted in errors", "Toggle", rowFailures);

            var (rowDedup, chkDedup) = ctx.Rows.AddToggle(panel,
                "Enable deduplication",
                "Avoid storing duplicate statements in quick succession");
            ctx.RegisterSearch("Enable deduplication", "Avoid storing duplicate statements in quick succession", "Toggle", rowDedup);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Storage");

            var (rowRetention, sldRetention, lblRetention) = ctx.Rows.AddSlider(panel,
                "Retention (days)", 1, 3650, 90,
                "Number of days to keep history entries before pruning");
            ctx.RegisterSearch("Retention (days)", "Number of days to keep history entries before pruning", "Slider", rowRetention);

            var (rowMax, sldMax, lblMax) = ctx.Rows.AddSlider(panel,
                "Max entries", 1000, 1_000_000, 100_000,
                "Maximum number of history entries stored", largeRange: true);
            ctx.RegisterSearch("Max entries", "Maximum number of history entries stored", "Slider", rowMax);

            var (rowEncrypt, chkEncrypt) = ctx.Rows.AddToggle(panel,
                "Encrypt at rest",
                "Encrypt stored SQL history using DPAPI + AES-256");
            ctx.RegisterSearch("Encrypt at rest", "Encrypt stored SQL history using DPAPI + AES-256", "Toggle", rowEncrypt);

            return new HistoryControls(chkEnabled, chkFailures, chkDedup, sldRetention, lblRetention, sldMax, lblMax, chkEncrypt);
        }
    }

    internal sealed class HistoryControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _recordFailures;
        private readonly CheckBox _deduplication;
        private readonly Slider _retentionDays;
        private readonly TextBlock _retentionLabel;
        private readonly Slider _maxEntries;
        private readonly TextBlock _maxEntriesLabel;
        private readonly CheckBox _encryptAtRest;

        public HistoryControls(CheckBox enabled, CheckBox failures, CheckBox dedup,
            Slider retention, TextBlock retentionLbl, Slider maxEntries, TextBlock maxEntriesLbl, CheckBox encrypt)
        {
            _enabled = enabled;
            _recordFailures = failures;
            _deduplication = dedup;
            _retentionDays = retention;
            _retentionLabel = retentionLbl;
            _maxEntries = maxEntries;
            _maxEntriesLabel = maxEntriesLbl;
            _encryptAtRest = encrypt;
        }

        public void Load(AppSettings settings)
        {
            var h = settings.History;
            _enabled.IsChecked = h.Enabled;
            _recordFailures.IsChecked = h.RecordFailures;
            _deduplication.IsChecked = h.Deduplication;
            _encryptAtRest.IsChecked = h.EncryptAtRest;
            _retentionDays.Value = h.RetentionDays;
            _retentionLabel.Text = h.RetentionDays.ToString(CultureInfo.InvariantCulture);
            _maxEntries.Value = h.MaxEntries;
            _maxEntriesLabel.Text = h.MaxEntries.ToString(CultureInfo.InvariantCulture);
        }

        public void Save(AppSettings settings)
        {
            settings.History.Enabled = _enabled.IsChecked == true;
            settings.History.RecordFailures = _recordFailures.IsChecked == true;
            settings.History.Deduplication = _deduplication.IsChecked == true;
            settings.History.EncryptAtRest = _encryptAtRest.IsChecked == true;
            settings.History.RetentionDays = (int)_retentionDays.Value;
            settings.History.MaxEntries = (int)_maxEntries.Value;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
