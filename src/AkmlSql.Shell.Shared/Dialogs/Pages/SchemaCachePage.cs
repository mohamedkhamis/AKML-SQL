#nullable enable
using System.Globalization;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class SchemaCachePage : IPageBuilder
    {
        public string Key     => "Schema Cache";
        public string Display => "Suggestions › Database";
        public string Title   => "Schema Cache";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Refresh Behavior");

            var (rowAuto, chkAuto) = ctx.Rows.AddToggle(panel,
                "Auto-refresh schema cache",
                "Periodically check for schema changes in the background");
            ctx.RegisterSearch("Auto-refresh schema cache", "Periodically check for schema changes in the background", "Toggle", rowAuto);

            var (rowRefresh, sldRefresh, lblRefresh) = ctx.Rows.AddSlider(panel,
                "Refresh interval (seconds)", 30, 3600, 300,
                "Time between background change-detection queries");
            ctx.RegisterSearch("Refresh interval (seconds)", "Time between background change-detection queries", "Slider", rowRefresh);

            var (rowDdl, chkDdl) = ctx.Rows.AddToggle(panel,
                "Detect DDL changes",
                "Trigger immediate cache refresh when DDL statements are executed");
            ctx.RegisterSearch("Detect DDL changes", "Trigger immediate cache refresh when DDL statements are executed", "Toggle", rowDdl);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Storage");

            var (rowMax, sldMax, lblMax) = ctx.Rows.AddSlider(panel,
                "Max cached databases", 1, 50, 10,
                "Number of database caches kept in memory before LRU eviction");
            ctx.RegisterSearch("Max cached databases", "Number of database caches kept in memory before LRU eviction", "Slider", rowMax);

            var (rowLazy, chkLazy) = ctx.Rows.AddToggle(panel,
                "Lazy-load column metadata",
                "Load columns and foreign keys in background (Phase B)");
            ctx.RegisterSearch("Lazy-load column metadata", "Load columns and foreign keys in background (Phase B)", "Toggle", rowLazy);

            var (rowPersist, chkPersist) = ctx.Rows.AddToggle(panel,
                "Persist cache to disk",
                "Save schema cache to disk for faster startup on reconnect");
            ctx.RegisterSearch("Persist cache to disk", "Save schema cache to disk for faster startup on reconnect", "Toggle", rowPersist);

            return new SchemaCacheControls(chkAuto, sldRefresh, lblRefresh, chkDdl, sldMax, lblMax, chkLazy, chkPersist);
        }
    }

    internal sealed class SchemaCacheControls : IPageControls
    {
        private readonly CheckBox _autoRefresh;
        private readonly Slider _refreshInterval;
        private readonly TextBlock _refreshIntervalLabel;
        private readonly CheckBox _detectDdl;
        private readonly Slider _maxDatabases;
        private readonly TextBlock _maxDatabasesLabel;
        private readonly CheckBox _lazyLoadColumns;
        private readonly CheckBox _persistToDisk;

        public SchemaCacheControls(CheckBox auto, Slider sldRefresh, TextBlock lblRefresh,
            CheckBox detectDdl, Slider sldMax, TextBlock lblMax, CheckBox lazyLoad, CheckBox persist)
        {
            _autoRefresh = auto;
            _refreshInterval = sldRefresh;
            _refreshIntervalLabel = lblRefresh;
            _detectDdl = detectDdl;
            _maxDatabases = sldMax;
            _maxDatabasesLabel = lblMax;
            _lazyLoadColumns = lazyLoad;
            _persistToDisk = persist;
        }

        public void Load(AppSettings settings)
        {
            var c = settings.Cache;
            _autoRefresh.IsChecked = c.AutoRefresh;
            _detectDdl.IsChecked = c.DetectDdl;
            _lazyLoadColumns.IsChecked = c.LazyLoadColumns;
            _persistToDisk.IsChecked = c.PersistToDisk;
            _refreshInterval.Value = c.RefreshIntervalSeconds;
            _refreshIntervalLabel.Text = c.RefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            _maxDatabases.Value = c.MaxDatabases;
            _maxDatabasesLabel.Text = c.MaxDatabases.ToString(CultureInfo.InvariantCulture);
        }

        public void Save(AppSettings settings)
        {
            settings.Cache.AutoRefresh = _autoRefresh.IsChecked == true;
            settings.Cache.DetectDdl = _detectDdl.IsChecked == true;
            settings.Cache.LazyLoadColumns = _lazyLoadColumns.IsChecked == true;
            settings.Cache.PersistToDisk = _persistToDisk.IsChecked == true;
            settings.Cache.RefreshIntervalSeconds = (int)_refreshInterval.Value;
            settings.Cache.MaxDatabases = (int)_maxDatabases.Value;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
