#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// General › SQL Server connections (the 2026-10 "rebalanced tree", design B): the SQL-auth
    /// credential settings, once SQL Prompt's "Connections &amp; memory" pane under Suggestions ›
    /// Lists &amp; connections. Spec 040 (OPT-01): the schema-cache memory settings changed
    /// nothing and are hidden; their saved values are kept. The key stays
    /// <c>ConnectionsMemory</c> so links, search and F1 keep resolving.
    /// </summary>
    internal sealed class ConnectionsMemoryPage : IPageBuilder
    {
        public string Key     => "ConnectionsMemory";
        public string Display => "General › SQL Server connections";
        public string Title   => "SQL Server connections";
        public string HelpTopic => "topics/options#sql-server-auth-connections";
        public string Help    => "How AKML SQL connects to load the schema for suggestions on SQL Server-auth windows: reuse of the password SSMS holds, and the passwords it has stored.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "SQL Server connections");

            var (rowSqlCreds, chkSqlCreds) = ctx.Rows.AddToggle(panel,
                "Use SQL Server-auth credentials for IntelliSense",
                "Reuse the password SSMS holds; Windows auth is unaffected");
            ctx.Rows.Tip(rowSqlCreds, "When on (default), AKML reuses the SQL password SSMS already holds for the connection — or a stored one — so SQL-auth windows get IntelliSense with no prompt. Off: SQL-auth windows are skipped. Windows / Azure AD connections are unaffected either way.");
            ctx.RegisterSearch("Use SQL Server-auth credentials for IntelliSense", "Reuse the SSMS-held or stored SQL password so SQL-auth windows get IntelliSense", "Toggle", rowSqlCreds);

            var (rowManageCreds, btnManageCreds) = ctx.Rows.AddButton(panel,
                "Saved SQL passwords",
                "Manage…",
                "Stored DPAPI-encrypted, per server and login");
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
        public ConnectionsMemoryControls(CheckBox sqlCreds)
        {
            EnableSqlAuthCredentials = sqlCreds;
        }

        /// <summary>The credentials toggle (test seam).</summary>
        internal CheckBox EnableSqlAuthCredentials { get; }

        public void Load(AppSettings settings)
        {
            EnableSqlAuthCredentials.IsChecked = settings.IntelliSense.EnableSqlAuthCredentials;
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.EnableSqlAuthCredentials = EnableSqlAuthCredentials.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
