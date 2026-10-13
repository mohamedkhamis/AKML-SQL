#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Queries › History › Recording (the 2026-10 "rebalanced tree", design B): recording and
    /// storage of the SQL history, in fitted rows. Restoring open queries on start is the page's
    /// second tab, <see cref="HistoryRestorePage"/>, greyed by Enable SQL history from here.
    /// The key stays <c>History</c> so links, search and F1 keep resolving.
    /// </summary>
    internal sealed class HistoryPage : IPageBuilder
    {
        public string Key     => "History";
        public string Display => "Queries › History › Recording";
        public string Title   => "Recording";
        public string HelpTopic => "topics/options#queries-history";
        public string Help    => "Recording of executed SQL statements to a local history database and how long that history is kept. Reopening the queries that were open when SSMS closed is on the Restoring queries tab.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Recording");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable SQL history", "Takes effect after SSMS restarts");
            ctx.Rows.Tip(rowEnabled, "Record all executed SQL statements to a local database. Takes effect after SSMS restarts.");
            ctx.RegisterSearch("Enable SQL history", "Record all executed SQL statements to a local database. Takes effect after SSMS restarts.", "Toggle", rowEnabled);

            // Spec 040 (HIS-06): "Record failed executions" and "Encrypt at rest" changed nothing and
            // are hidden; their saved values are kept.

            var (left, right) = ctx.Rows.AddColumns(panel);

            var (rowDedup, chkDedup) = ctx.Rows.AddToggle(left,
                "Group repeated runs of the same query", "One row per query tab", parent: chkEnabled);
            ctx.Rows.Tip(rowDedup, "Show one row per query tab, with its runs and versions inside.");
            ctx.RegisterSearch("Group repeated runs of the same query", "Show one row per query tab, with its runs and versions inside.", "Toggle", rowDedup);

            var (rowRemember, chkRemember) = ctx.Rows.AddToggle(right,
                "Remember advanced search settings", "", parent: chkEnabled);
            ctx.Rows.Tip(rowRemember, "Keep the Advanced search filters of the SQL History window between sessions.");
            ctx.RegisterSearch("Remember advanced search settings", "Keep the Advanced search filters of the SQL History window between sessions.", "Toggle", rowRemember);

            // Spec 040 (HIS-13): longer queries are kept up to this size, with a note at the end.
            var (rowQuerySize, numQuerySize) = ctx.Rows.AddNumber(panel,
                "Maximum query size", 16, 1024, 16, "KB",
                "Longer queries are cut, with a note at the end", chkEnabled);
            ctx.RegisterSearch("Maximum query size", "Largest query text kept. Longer queries are cut, with a note at the end.", "Number", rowQuerySize);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Storage");

            var (storeLeft, storeRight) = ctx.Rows.AddColumns(panel);

            var (rowRetention, numRetention) = ctx.Rows.AddNumber(storeLeft,
                "Retention", 1, 3650, 1, "days", "", chkEnabled);
            ctx.Rows.Tip(rowRetention, "Number of days to keep history entries before pruning. Takes effect after SSMS restarts.");
            ctx.RegisterSearch("Retention", "Number of days to keep history entries before pruning. Takes effect after SSMS restarts.", "Number", rowRetention);

            var (rowMax, numMax) = ctx.Rows.AddNumber(storeRight,
                "Max entries", 1000, 1_000_000, 1000, "entries", "", chkEnabled);
            ctx.Rows.Tip(rowMax, "Maximum number of history entries stored. Takes effect after SSMS restarts.");
            ctx.RegisterSearch("Max entries", "Maximum number of history entries stored. Takes effect after SSMS restarts.", "Number", rowMax);

            var (rowDisableTrim, chkDisableTrim) = ctx.Rows.AddToggle(panel,
                "Disable automatic history trimming", "Never purge by days or entries", parent: chkEnabled);
            ctx.Rows.Tip(rowDisableTrim, "Keep all history entries and version snapshots — never purge based on retention days or max entries. Takes effect after SSMS restarts.");
            ctx.RegisterSearch("Disable automatic history trimming", "Keep all history entries and version snapshots — never purge based on retention days or max entries. Takes effect after SSMS restarts.", "Toggle", rowDisableTrim);

            return new HistoryControls(chkEnabled, chkDedup, numRetention, numMax, chkDisableTrim)
            {
                QuerySize = numQuerySize,
                RememberAdvancedSearch = chkRemember,
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

        /// <summary>The master switch — the Restoring queries tab's rows grey with it (test seam).</summary>
        internal CheckBox Enabled => _enabled;

        // Spec 040 (HIS-13) rows.
        internal TextBox? QuerySize { get; set; }
        internal CheckBox? RememberAdvancedSearch { get; set; }

        public void Load(AppSettings settings)
        {
            var h = settings.History;
            _enabled.IsChecked = h.Enabled;
            _deduplication.IsChecked = h.Deduplication;
            _disableAutoTrim.IsChecked = h.DisableAutoTrim;
            RowFactory.SetNumber(_retentionDays, h.RetentionDays);
            RowFactory.SetNumber(_maxEntries, h.MaxEntries);
            if (QuerySize != null) RowFactory.SetNumber(QuerySize, h.MaxQuerySizeKb);
            if (RememberAdvancedSearch != null) RememberAdvancedSearch.IsChecked = h.RememberAdvancedSearch;
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
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
