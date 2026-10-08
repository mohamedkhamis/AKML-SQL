#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Connections &amp; Memory — mirrors SQL Prompt's "Connections &amp; memory" pane
    /// (report §4 rec #5): the SQL-auth credential settings. Spec 040 (OPT-01): the schema-cache
    /// memory settings (cached-database count, background column loading, persist to disk)
    /// changed nothing and are hidden; their saved values are kept.
    /// </summary>
    internal sealed class ConnectionsMemoryPage : IPageBuilder
    {
        public string Key     => "ConnectionsMemory";
        public string Display => "Suggestions › Lists & connections › SQL Server-auth connections";
        public string Title   => "SQL Server-auth connections";
        public string HelpTopic => "topics/options#sql-server-auth-connections";
        public string Help    => "Controls how AKML SQL connects to load the schema for suggestions, including reuse of SQL Server-auth passwords so SQL-auth windows get IntelliSense.";

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

            return new ConnectionsMemoryControls(chkSqlCreds);
        }
    }

    internal sealed class ConnectionsMemoryControls : IPageControls
    {
        private readonly CheckBox _enableSqlAuthCreds;

        public ConnectionsMemoryControls(CheckBox sqlCreds)
        {
            _enableSqlAuthCreds = sqlCreds;
        }

        public void Load(AppSettings settings)
        {
            _enableSqlAuthCreds.IsChecked = settings.IntelliSense.EnableSqlAuthCredentials;
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.EnableSqlAuthCredentials = _enableSqlAuthCreds.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
