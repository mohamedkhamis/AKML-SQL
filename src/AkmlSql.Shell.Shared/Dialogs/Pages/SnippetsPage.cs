#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Snippets page (Phase 2 B.2 — first migration). Mirrors the previous
    /// <c>SettingsWindow.BuildSnippetsPage</c> body and the inline Snippets blocks
    /// of <c>LoadSettingsToControls</c> / <c>SaveControlsToSettings</c>.
    /// </summary>
    internal sealed class SnippetsPage : IPageBuilder
    {
        public string Key     => "Snippets";
        public string Display => "Snippets";
        public string Title   => "Snippets";
        public string Help    => "Configure the snippet engine: enable snippets, show them in IntelliSense completions, format after expansion, filter by SQL context, and track usage for ranking. Set the personal and team folders where your .akmlsnippet files live.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Snippet Manager");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable snippets",
                "Master switch for the snippet engine");
            ctx.RegisterSearch("Enable snippets", "Master switch for the snippet engine", "Toggle", rowEnabled);

            var (rowShowInCompletion, chkShowInCompletion) = ctx.Rows.AddToggle(panel,
                "Show in IntelliSense completions",
                "Include snippets in the main completion list");
            ctx.RegisterSearch("Show in IntelliSense completions", "Include snippets in the main completion list", "Toggle", rowShowInCompletion);

            var (rowFormatOnExpand, chkFormatOnExpand) = ctx.Rows.AddToggle(panel,
                "Format after expansion",
                "Apply SQL formatting after expanding a snippet");
            ctx.RegisterSearch("Format after expansion", "Apply SQL formatting after expanding a snippet", "Toggle", rowFormatOnExpand);

            var (rowContextFilter, chkContextFilter) = ctx.Rows.AddToggle(panel,
                "Filter by SQL context",
                "Only show snippets valid for the current SQL position");
            ctx.RegisterSearch("Filter by SQL context", "Only show snippets valid for the current SQL position", "Toggle", rowContextFilter);

            var (rowTrackUsage, chkTrackUsage) = ctx.Rows.AddToggle(panel,
                "Track usage for ranking",
                "Boost frequently-used snippets to the top of the list");
            ctx.RegisterSearch("Track usage for ranking", "Boost frequently-used snippets to the top of the list", "Toggle", rowTrackUsage);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Snippet Folders");

            var (rowPersonalFolder, txtPersonalFolder) = ctx.Rows.AddTextInput(panel,
                "Personal folder",
                "Path to personal .akmlsnippet files (leave empty for default)");
            ctx.RegisterSearch("Personal folder", "Path to personal .akmlsnippet files (leave empty for default)", "Text", rowPersonalFolder);

            var (rowTeamFolder, txtTeamFolder) = ctx.Rows.AddTextInput(panel,
                "Team folder",
                "Shared folder for team snippet distribution");
            ctx.RegisterSearch("Team folder", "Shared folder for team snippet distribution", "Text", rowTeamFolder);

            return new SnippetsControls(
                chkEnabled, chkShowInCompletion, chkFormatOnExpand, chkContextFilter, chkTrackUsage,
                txtPersonalFolder, txtTeamFolder);
        }
    }

    internal sealed class SnippetsControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _showInCompletion;
        private readonly CheckBox _formatOnExpand;
        private readonly CheckBox _contextFilter;
        private readonly CheckBox _trackUsage;
        private readonly TextBox _personalFolder;
        private readonly TextBox _teamFolder;

        public SnippetsControls(
            CheckBox enabled, CheckBox showInCompletion, CheckBox formatOnExpand,
            CheckBox contextFilter, CheckBox trackUsage,
            TextBox personalFolder, TextBox teamFolder)
        {
            _enabled = enabled;
            _showInCompletion = showInCompletion;
            _formatOnExpand = formatOnExpand;
            _contextFilter = contextFilter;
            _trackUsage = trackUsage;
            _personalFolder = personalFolder;
            _teamFolder = teamFolder;
        }

        public void Load(AppSettings settings)
        {
            var s = settings.Snippets;
            _enabled.IsChecked = s.Enabled;
            _showInCompletion.IsChecked = s.ShowInCompletion;
            _formatOnExpand.IsChecked = s.FormatOnExpand;
            _contextFilter.IsChecked = s.ContextFilter;
            _trackUsage.IsChecked = s.TrackUsage;
            _personalFolder.Text = s.PersonalFolder ?? string.Empty;
            _teamFolder.Text = s.TeamFolder ?? string.Empty;
        }

        public void Save(AppSettings settings)
        {
            settings.Snippets.Enabled = _enabled.IsChecked == true;
            settings.Snippets.ShowInCompletion = _showInCompletion.IsChecked == true;
            settings.Snippets.FormatOnExpand = _formatOnExpand.IsChecked == true;
            settings.Snippets.ContextFilter = _contextFilter.IsChecked == true;
            settings.Snippets.TrackUsage = _trackUsage.IsChecked == true;
            settings.Snippets.PersonalFolder = _personalFolder.Text ?? string.Empty;
            settings.Snippets.TeamFolder = _teamFolder.Text ?? string.Empty;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
