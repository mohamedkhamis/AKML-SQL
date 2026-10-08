#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Snippets page (Phase 2 B.2 — first migration). Spec 040 (OPT-01): the duplicate "Show in
    /// IntelliSense completions" (the working switch is on Suggestions › Behavior), "Filter by SQL
    /// context", "Track usage for ranking" and "Personal folder" changed nothing and are hidden;
    /// their saved values are kept.
    /// </summary>
    internal sealed class SnippetsPage : IPageBuilder
    {
        public string Key     => "Snippets";
        public string Display => "Suggestions › Lists & connections › Snippets";
        public string Title   => "Snippets";
        public string HelpTopic => "topics/options#snippets";
        public string Help    => "Configure the snippet engine: enable snippets and format them after expansion. Set the team folder where shared .akmlsnippet files live. Whether snippets appear in the suggestions box is set on Suggestions › Behavior.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Snippet manager");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable snippets",
                "Master switch for the snippet engine");
            ctx.RegisterSearch("Enable snippets", "Master switch for the snippet engine", "Toggle", rowEnabled);

            var (rowFormatOnExpand, chkFormatOnExpand) = ctx.Rows.AddToggle(panel,
                "Format after expansion",
                "Apply SQL formatting after expanding a snippet", chkEnabled);
            ctx.RegisterSearch("Format after expansion", "Apply SQL formatting after expanding a snippet", "Toggle", rowFormatOnExpand);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Snippet folders");

            var (rowTeamFolder, txtTeamFolder) = ctx.Rows.AddTextInput(panel,
                "Team folder",
                "Shared folder for team snippet distribution. Takes effect after SSMS restarts.", parent: chkEnabled);
            ctx.RegisterSearch("Team folder", "Shared folder for team snippet distribution. Takes effect after SSMS restarts.", "Text", rowTeamFolder);

            return new SnippetsControls(chkEnabled, chkFormatOnExpand, txtTeamFolder);
        }
    }

    internal sealed class SnippetsControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _formatOnExpand;
        private readonly TextBox _teamFolder;

        public SnippetsControls(CheckBox enabled, CheckBox formatOnExpand, TextBox teamFolder)
        {
            _enabled = enabled;
            _formatOnExpand = formatOnExpand;
            _teamFolder = teamFolder;
        }

        public void Load(AppSettings settings)
        {
            var s = settings.Snippets;
            _enabled.IsChecked = s.Enabled;
            _formatOnExpand.IsChecked = s.FormatOnExpand;
            _teamFolder.Text = s.TeamFolder ?? string.Empty;
        }

        public void Save(AppSettings settings)
        {
            settings.Snippets.Enabled = _enabled.IsChecked == true;
            settings.Snippets.FormatOnExpand = _formatOnExpand.IsChecked == true;
            settings.Snippets.TeamFolder = _teamFolder.Text ?? string.Empty;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
