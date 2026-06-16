#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Inserted Code › JOIN completion (Phase 2 C.4). Surfaces
    /// <see cref="JoinOptionsSettings"/>. <c>MatchByColumnName</c> is read by
    /// <c>JoinOnFkProvider</c> via <c>CompletionEngine</c> (A.4).
    ///
    /// Note: the related <c>JoinAssist</c> and <c>AutoAlias</c> toggles are
    /// surfaced on the IntelliSense (Behavior) page already — this page is just
    /// the new <c>MatchByColumnName</c> toggle plus a non-clickable info row
    /// pointing readers at where the related toggles live.
    /// </summary>
    internal sealed class JoinCompletionPage : IPageBuilder
    {
        public string Key     => "JoinOptions";
        public string Display => "Inserted Code › JOIN completion";
        public string Title   => "JOIN completion";
        public string Help    => "Controls how JOIN completion suggests ON conditions, including whether to fall back to matching column names when no foreign key links the two tables.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "FK fallback");

            var (rowMatch, chkMatch) = ctx.Rows.AddToggle(panel,
                "Use matching column names when no FK exists",
                "When two tables have a same-named column (e.g. both have 'Id') but no foreign key, suggest joining on that column. With this off, JOIN completion only fires when an FK is present.");
            ctx.RegisterSearch("Use matching column names when no FK exists", "Match by column name fallback for JOIN ON suggestions", "Toggle", rowMatch);

            ctx.Rows.AddGroupSeparator(panel);
            var related = ctx.Rows.AddInfoRow(panel,
                "Related",
                "JOIN suggestions and aliases are configured under Suggestions › Behavior (JOIN clause assistance, Tables Alias).");
            ctx.RegisterSearch("Related", "Pointer to JOIN clause assistance and Tables Alias on the Suggestions › Behavior page", "Info", related);

            return new JoinCompletionControls(chkMatch);
        }
    }

    internal sealed class JoinCompletionControls : IPageControls
    {
        private readonly CheckBox _matchByColumnName;

        public JoinCompletionControls(CheckBox matchByColumnName)
        {
            _matchByColumnName = matchByColumnName;
        }

        public void Load(AppSettings settings)
        {
            _matchByColumnName.IsChecked = settings.IntelliSense.JoinOptions.MatchByColumnName;
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.JoinOptions.MatchByColumnName = _matchByColumnName.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
