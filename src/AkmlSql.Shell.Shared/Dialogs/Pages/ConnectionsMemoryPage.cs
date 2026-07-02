#nullable enable
using System.Globalization;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Connections &amp; Memory — mirrors SQL Prompt's "Connections &amp; memory" pane
    /// (report §4 rec #5). Consolidates the SQL-auth credential settings (previously on
    /// Suggestions › Behavior) with the schema-cache storage/memory knobs (previously on
    /// Suggestions › Database, which keeps its refresh-behavior settings).
    /// </summary>
    internal sealed class ConnectionsMemoryPage : IPageBuilder
    {
        public string Key     => "ConnectionsMemory";
        public string Display => "Connections & Memory";
        public string Title   => "Connections & Memory";
        public string Help    => "Controls how AKML SQL connects for schema loading — including reuse of SQL Server-auth passwords so SQL-auth windows get IntelliSense — and how much memory the schema cache may use: cached-database count, background column loading, and persisting the cache to disk.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Connections");

            var (rowSqlCreds, chkSqlCreds) = ctx.Rows.AddToggle(panel,
                "Use SQL Server-auth credentials for IntelliSense",
                "When on (default), AKML reuses the SQL password SSMS already holds for the connection — or a stored one — so SQL-auth windows get IntelliSense with no prompt. Off: SQL-auth windows are skipped. Windows / Azure AD connections are unaffected either way.");
            ctx.RegisterSearch("Use SQL Server-auth credentials for IntelliSense", "Reuse the SSMS-held or stored SQL password so SQL-auth windows get IntelliSense", "Toggle", rowSqlCreds);

            var (rowManageCreds, btnManageCreds) = ctx.Rows.AddButton(panel,
                "Saved SQL passwords",
                "Manage…",
                "View and remove the SQL passwords AKML has stored (DPAPI-encrypted, per server + login).");
            ctx.RegisterSearch("Saved SQL passwords", "View and remove stored SQL passwords", "Button", rowManageCreds);
            btnManageCreds.Click += (_, _) =>
            {
                try { new Editor.SqlCredentialManagerDialog().ShowDialog(); }
                catch { /* opening the manager is non-critical */ }
            };

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Memory & cache");

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

            return new ConnectionsMemoryControls(chkSqlCreds, sldMax, lblMax, chkLazy, chkPersist);
        }
    }

    internal sealed class ConnectionsMemoryControls : IPageControls
    {
        private readonly CheckBox _enableSqlAuthCreds;
        private readonly Slider _maxDatabases;
        private readonly TextBlock _maxDatabasesLabel;
        private readonly CheckBox _lazyLoadColumns;
        private readonly CheckBox _persistToDisk;

        public ConnectionsMemoryControls(CheckBox sqlCreds, Slider sldMax, TextBlock lblMax,
            CheckBox lazyLoad, CheckBox persist)
        {
            _enableSqlAuthCreds = sqlCreds;
            _maxDatabases = sldMax;
            _maxDatabasesLabel = lblMax;
            _lazyLoadColumns = lazyLoad;
            _persistToDisk = persist;
        }

        public void Load(AppSettings settings)
        {
            _enableSqlAuthCreds.IsChecked = settings.IntelliSense.EnableSqlAuthCredentials;
            _maxDatabases.Value = settings.Cache.MaxDatabases;
            _maxDatabasesLabel.Text = settings.Cache.MaxDatabases.ToString(CultureInfo.InvariantCulture);
            _lazyLoadColumns.IsChecked = settings.Cache.LazyLoadColumns;
            _persistToDisk.IsChecked = settings.Cache.PersistToDisk;
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.EnableSqlAuthCredentials = _enableSqlAuthCreds.IsChecked == true;
            settings.Cache.MaxDatabases = (int)_maxDatabases.Value;
            settings.Cache.LazyLoadColumns = _lazyLoadColumns.IsChecked == true;
            settings.Cache.PersistToDisk = _persistToDisk.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
