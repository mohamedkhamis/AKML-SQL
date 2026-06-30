#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Miscellaneous › Labs (Phase 2 C.5). Surfaces <see cref="LabsSettings"/>
    /// experimental flags. Features under Labs may change or be removed without
    /// notice — flagged in the page banner.
    /// </summary>
    internal sealed class LabsPage : IPageBuilder
    {
        public string Key     => "Labs";
        public string Display => "Miscellaneous › Labs";
        public string Title   => "Labs";
        public string Help    => "Enable or disable experimental features such as ghost-text AI completion, parallel schema-cache loading, and shared snippet sync. These features are unstable and may change or be removed without notice.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            var notice = ctx.Rows.AddInfoRow(panel,
                "⚠ Labs notice",
                "Features under Labs are experimental and may change or be removed without notice. Use only in non-production environments.");
            ctx.RegisterSearch("Labs notice", "Experimental features warning", "Info", notice);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Experimental features");

            var (rowGhost, chkGhost) = ctx.Rows.AddToggle(panel,
                "Ghost-text AI completion",
                "Show inline grey-text AI suggestions as you type. Requires AI Assistance to be configured.");
            ctx.RegisterSearch("Ghost-text AI completion", "Inline AI suggestion ghost text", "Toggle", rowGhost);

            var (rowParallel, chkParallel) = ctx.Rows.AddToggle(panel,
                "Parallel schema cache",
                "Load Phase A and Phase B schema metadata in parallel. May reduce first-completion latency on large databases.");
            ctx.RegisterSearch("Parallel schema cache", "Parallel Phase A/B schema metadata loading", "Toggle", rowParallel);

            var (rowSnippetSync, chkSnippetSync) = ctx.Rows.AddToggle(panel,
                "Shared snippet sync",
                "Sync snippet folders across machines via the configured team folder. Future-pending.");
            ctx.RegisterSearch("Shared snippet sync", "Cross-machine snippet sync via team folder", "Toggle", rowSnippetSync);

            return new LabsControls(chkGhost, chkParallel, chkSnippetSync);
        }
    }

    internal sealed class LabsControls : IPageControls
    {
        private readonly CheckBox _ghostText;
        private readonly CheckBox _parallelSchemaCache;
        private readonly CheckBox _sharedSnippetSync;

        public LabsControls(CheckBox ghost, CheckBox parallel, CheckBox snippet)
        {
            _ghostText = ghost;
            _parallelSchemaCache = parallel;
            _sharedSnippetSync = snippet;
        }

        public void Load(AppSettings settings)
        {
            var l = settings.Labs;
            _ghostText.IsChecked = l.GhostTextCompletion;
            _parallelSchemaCache.IsChecked = l.ParallelSchemaCache;
            _sharedSnippetSync.IsChecked = l.SharedSnippetSync;
        }

        public void Save(AppSettings settings)
        {
            var l = settings.Labs;
            l.GhostTextCompletion = _ghostText.IsChecked == true;
            l.ParallelSchemaCache = _parallelSchemaCache.IsChecked == true;
            l.SharedSnippetSync = _sharedSnippetSync.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
