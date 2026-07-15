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
        public string Help    => "Controls how the database schema cache is kept current: background auto-refresh interval and immediate DDL-change detection. Storage and memory limits (cached-database count, lazy column loading, persist-to-disk) live on the Connections & Memory page.";

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

            // Storage / memory rows moved to ConnectionsMemoryPage (SQL Prompt "Connections & memory").

            return new SchemaCacheControls(chkAuto, sldRefresh, lblRefresh, chkDdl);
        }
    }

    internal sealed class SchemaCacheControls : IPageControls
    {
        private readonly CheckBox _autoRefresh;
        private readonly Slider _refreshInterval;
        private readonly TextBlock _refreshIntervalLabel;
        private readonly CheckBox _detectDdl;

        public SchemaCacheControls(CheckBox auto, Slider sldRefresh, TextBlock lblRefresh, CheckBox detectDdl)
        {
            _autoRefresh = auto;
            _refreshInterval = sldRefresh;
            _refreshIntervalLabel = lblRefresh;
            _detectDdl = detectDdl;
        }

        public void Load(AppSettings settings)
        {
            var c = settings.Cache;
            _autoRefresh.IsChecked = c.AutoRefresh;
            _detectDdl.IsChecked = c.DetectDdl;
            _refreshInterval.Value = c.RefreshIntervalSeconds;
            _refreshIntervalLabel.Text = c.RefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        }

        public void Save(AppSettings settings)
        {
            settings.Cache.AutoRefresh = _autoRefresh.IsChecked == true;
            settings.Cache.DetectDdl = _detectDdl.IsChecked == true;
            settings.Cache.RefreshIntervalSeconds = (int)_refreshInterval.Value;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
