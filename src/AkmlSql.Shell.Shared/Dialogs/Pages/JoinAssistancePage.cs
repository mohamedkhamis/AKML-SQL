#nullable enable
using System.Collections.Generic;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Suggestions › Behavior › JOIN assistance (the 2026-10 "rebalanced tree", design B): the
    /// three JOIN switches in one place — FK-assisted JOIN completion and alias generation (once
    /// an "Assistance" group of the Completion page) and the same-named-column fallback (once the
    /// one-row "Join conditions" page, whose "Related" note pointed at the page it was on).
    /// <c>JoinAssist</c> and <c>AutoAlias</c> are read by <c>CompletionEngine</c>;
    /// <c>MatchByColumnName</c> by <c>JoinOnFkProvider</c>.
    /// The key stays <c>JoinOptions</c> so links, search and F1 keep resolving.
    /// </summary>
    internal sealed class JoinAssistancePage : IPageBuilder
    {
        public string Key     => "JoinOptions";
        public string Display => "Suggestions › Behavior › JOIN assistance";
        public string Title   => "JOIN assistance";
        public string HelpTopic => "topics/options#join-conditions";
        public string Help    => "How JOIN completion helps: FK-related tables first after JOIN with the ON clause filled in, aliases for the tables it inserts, and joining on a same-named column when no foreign key links the two tables.";

        /// <summary>
        /// The Enable IntelliSense switch on the Completion tab, handed over by
        /// <see cref="SuggestionsBehaviorPage"/> before this section builds: every row here
        /// greys with it, as it did when the rows sat on the Completion page.
        /// </summary>
        internal CheckBox? MasterSwitch { get; set; }

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "JOIN assistance");

            var (rowJoin, chkJoin) = ctx.Rows.AddToggle(panel,
                "JOIN clause assistance", "FK-related tables first after JOIN, with the ON clause filled in", MasterSwitch);
            ctx.Rows.Tip(rowJoin, "Master switch for FK-assisted JOIN completion. When on: after typing 'JOIN', FK-related tables are suggested first with a full ON clause inserted; inside 'ON', ready-made FK equality predicates are suggested. Independent of Suggest table aliases. Default: on.");
            ctx.RegisterSearch("JOIN clause assistance", "Master switch for FK-assisted JOIN completion", "Toggle", rowJoin);

            var (left, right) = ctx.Rows.AddColumns(panel);

            var (rowAlias, chkAlias) = ctx.Rows.AddToggle(left,
                "Suggest table aliases", "Generate aliases for inserted tables (Orders o)", MasterSwitch);
            ctx.Rows.Tip(rowAlias, "When on, completion generates new aliases for inserted tables (e.g. 'Orders o ON o.CustomerId = c.Id'). When off, FK JOIN suggestions still fire but the target table is referenced by its bare name ('Orders ON Orders.CustomerId = c.Id'). Default: off.");
            ctx.RegisterSearch("Suggest table aliases", "Generate new aliases for inserted tables in JOIN completions", "Toggle", rowAlias);

            var (rowMatch, chkMatch) = ctx.Rows.AddToggle(right,
                "Use matching column names when no FK exists", "Join on a same-named column when there is no FK", MasterSwitch);
            ctx.Rows.Tip(rowMatch, "When two tables have a same-named column (e.g. both have 'Id') but no foreign key, suggest joining on that column. With this off, JOIN completion only fires when an FK is present.");
            ctx.RegisterSearch("Use matching column names when no FK exists", "Match by column name fallback for JOIN ON suggestions", "Toggle", rowMatch);

            return new JoinAssistanceControls(chkJoin, chkAlias, chkMatch, new[] { rowJoin, rowAlias, rowMatch });
        }
    }

    internal sealed class JoinAssistanceControls : IPageControls
    {
        public JoinAssistanceControls(CheckBox joinAssist, CheckBox autoAlias, CheckBox matchByColumnName, IReadOnlyList<Border> rows)
        {
            JoinAssist = joinAssist;
            AutoAlias = autoAlias;
            MatchByColumnName = matchByColumnName;
            Rows = rows;
        }

        internal CheckBox JoinAssist { get; }
        internal CheckBox AutoAlias { get; }
        internal CheckBox MatchByColumnName { get; }

        /// <summary>The three rows, for the greying check (test seam).</summary>
        internal IReadOnlyList<Border> Rows { get; }

        public void Load(AppSettings settings)
        {
            JoinAssist.IsChecked = settings.IntelliSense.JoinAssist;
            AutoAlias.IsChecked = settings.IntelliSense.AutoAlias;
            MatchByColumnName.IsChecked = settings.IntelliSense.JoinOptions.MatchByColumnName;
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.JoinAssist = JoinAssist.IsChecked == true;
            settings.IntelliSense.AutoAlias = AutoAlias.IsChecked == true;
            settings.IntelliSense.JoinOptions.MatchByColumnName = MatchByColumnName.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
