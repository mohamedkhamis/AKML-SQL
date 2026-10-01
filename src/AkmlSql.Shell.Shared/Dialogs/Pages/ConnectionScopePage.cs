#nullable enable
using System;
using System.Linq;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Spec 030 T082 (FR-043) — Options UI for the suggestion connection scope shipped in T036
    /// (<see cref="ConnectionScopeSettings"/>): limit the object-suggestion list to a set of
    /// databases and/or schemas, and whether linked servers are suggested.
    /// Database/schema lists are edited as comma-separated text; empty means “no restriction”.
    /// </summary>
    internal sealed class ConnectionScopePage : IPageBuilder
    {
        public string Key     => "ConnectionScope";
        public string Display => "Suggestions › Connections";
        public string Title   => "Connections";
        public string HelpTopic => "topics/options#suggestions-connections";
        public string Help    => "Narrow the object-suggestion list to specific databases and/or schemas (leave a field empty for no restriction), and choose whether linked servers are suggested after FROM and JOIN.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Suggestion scope");

            var (rowDatabases, txtDatabases) = ctx.Rows.AddTextInput(panel,
                "Limit databases to",
                "Comma-separated database names. Only suggest objects from these databases. Leave empty to allow the connected database.");
            ctx.RegisterSearch("Limit databases to", "Only suggest objects from these databases (comma-separated; empty = no restriction)", "Text", rowDatabases);

            var (rowSchemas, txtSchemas) = ctx.Rows.AddTextInput(panel,
                "Limit schemas to",
                "Comma-separated schema names (case-insensitive). Only suggest objects from these schemas. Leave empty to allow all schemas.");
            ctx.RegisterSearch("Limit schemas to", "Only suggest objects from these schemas (comma-separated; empty = all)", "Text", rowSchemas);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Linked servers");

            var (rowLinked, chkLinked) = ctx.Rows.AddToggle(panel,
                "Include linked-server objects in suggestions",
                "Suggest the server's linked servers after FROM and JOIN, so you can write four-part names.");
            ctx.RegisterSearch("Include linked-server objects in suggestions", "Suggest the server's linked servers after FROM and JOIN", "Toggle", rowLinked);

            return new ConnectionScopeControls(txtDatabases, txtSchemas, chkLinked);
        }
    }

    internal sealed class ConnectionScopeControls : IPageControls
    {
        private readonly TextBox _databases;
        private readonly TextBox _schemas;
        private readonly CheckBox _includeLinkedServers;

        public ConnectionScopeControls(TextBox databases, TextBox schemas, CheckBox includeLinkedServers)
        {
            _databases = databases;
            _schemas = schemas;
            _includeLinkedServers = includeLinkedServers;
        }

        public void Load(AppSettings settings)
        {
            var s = settings.IntelliSense.ConnectionScope;
            _databases.Text = string.Join(", ", s.Databases ?? Array.Empty<string>());
            _schemas.Text = string.Join(", ", s.Schemas ?? Array.Empty<string>());
            _includeLinkedServers.IsChecked = s.IncludeLinkedServers;
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.ConnectionScope.Databases = ParseCsv(_databases.Text);
            settings.IntelliSense.ConnectionScope.Schemas = ParseCsv(_schemas.Text);
            settings.IntelliSense.ConnectionScope.IncludeLinkedServers = _includeLinkedServers.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);

        private static string[] ParseCsv(string? text)
            => (text ?? string.Empty)
                .Split(',')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}
