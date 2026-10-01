#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class IntelliSensePage : IPageBuilder
    {
        public string Key     => "IntelliSense";
        public string Display => "Suggestions › Behavior";
        public string Title   => "Behavior";
        public string HelpTopic => "topics/options#suggestions-behavior";
        public string Help    => "Controls AKML SQL completion behavior — auto-triggering, fuzzy matching, suggestion count and trigger delay, column/PK/FK details, popup Ctrl-transparency, FK-assisted JOIN and alias generation, commit keys, and snippets. Special-character handling lives on Inserted Code › Special characters; SQL-auth credentials on Connections & Memory.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Core");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable IntelliSense", "Master switch for all IntelliSense features");
            ctx.RegisterSearch("Enable IntelliSense", "Master switch for all IntelliSense features", "Toggle", rowEnabled);

            var (rowAutoTrig, chkAutoTrig) = ctx.Rows.AddToggle(panel,
                "Auto-trigger completions while typing",
                "Show completion list automatically without Ctrl+Space", chkEnabled);
            ctx.RegisterSearch("Auto-trigger completions while typing", "Show completion list automatically without Ctrl+Space", "Toggle", rowAutoTrig);

            var (rowAfterDot, chkAfterDot) = ctx.Rows.AddToggle(panel,
                "Trigger after dot",
                "Auto-complete after typing '.' for table.column references", chkEnabled);
            ctx.RegisterSearch("Trigger after dot", "Auto-complete after typing '.' for table.column references", "Toggle", rowAfterDot);

            var (rowFuzzy, chkFuzzy) = ctx.Rows.AddToggle(panel,
                "Enable fuzzy matching",
                "Substring and approximate matching in addition to prefix", chkEnabled);
            ctx.RegisterSearch("Enable fuzzy matching", "Substring and approximate matching in addition to prefix", "Toggle", rowFuzzy);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Display");

            // Spec 040 (OPT-06): number fields, not sliders, for wide ranges.
            var (rowMaxSugg, numMaxSugg) = ctx.Rows.AddNumber(panel,
                "Maximum suggestions", 5, 200, 5, "items",
                "Maximum number of items shown in the completion list", chkEnabled);
            ctx.RegisterSearch("Maximum suggestions", "Maximum number of items shown in the completion list", "Number", rowMaxSugg);

            var (rowTrigDelay, numTrigDelay) = ctx.Rows.AddNumber(panel,
                "Trigger delay", 0, 2000, 50, "ms",
                "Debounce delay before showing completions", chkEnabled);
            ctx.RegisterSearch("Trigger delay", "Debounce delay before showing completions", "Number", rowTrigDelay);

            // Spec 040 (OPT-01): "Keyword casing" is hidden — nothing reads it; the saved value is kept.

            var (rowDataTypes, chkDataTypes) = ctx.Rows.AddToggle(panel,
                "Show column data types",
                "Display data type information in completion details", chkEnabled);
            ctx.RegisterSearch("Show column data types", "Display data type information in completion details", "Toggle", rowDataTypes);

            var (rowNullable, chkNullable) = ctx.Rows.AddToggle(panel,
                "Show nullability info",
                "Show NOT NULL / NULL status in completion details", chkEnabled);
            ctx.RegisterSearch("Show nullability info", "Show NOT NULL / NULL status in completion details", "Toggle", rowNullable);

            var (rowPkFk, chkPkFk) = ctx.Rows.AddToggle(panel,
                "Show PK/FK indicators",
                "Show primary key and foreign key badges", chkEnabled);
            ctx.RegisterSearch("Show PK/FK indicators", "Show primary key and foreign key badges", "Toggle", rowPkFk);

            var (rowCtrlTransparent, chkCtrlTransparent) = ctx.Rows.AddToggle(panel,
                "Make popups transparent when Ctrl is held",
                "Hold Ctrl to see the code underneath the completion popup", chkEnabled);
            ctx.RegisterSearch("Make popups transparent when Ctrl is held", "Hold Ctrl to see the code underneath the completion popup", "Toggle", rowCtrlTransparent);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Assistance");

            var (rowJoin, chkJoin) = ctx.Rows.AddToggle(panel,
                "JOIN clause assistance",
                "Master switch for FK-assisted JOIN completion. When on: after typing 'JOIN', FK-related tables are suggested first with a full ON clause inserted; inside 'ON', ready-made FK equality predicates are suggested. Independent of Suggest table aliases. Default: on.", chkEnabled);
            ctx.RegisterSearch("JOIN clause assistance", "Master switch for FK-assisted JOIN completion", "Toggle", rowJoin);

            var (rowAlias, chkAlias) = ctx.Rows.AddToggle(panel,
                "Suggest table aliases",
                "When on, completion generates new aliases for inserted tables (e.g. 'Orders o ON o.CustomerId = c.Id'). When off, FK JOIN suggestions still fire but the target table is referenced by its bare name ('Orders ON Orders.CustomerId = c.Id'). Default: off.", chkEnabled);
            ctx.RegisterSearch("Suggest table aliases", "Generate new aliases for inserted tables in JOIN completions", "Toggle", rowAlias);

            var (rowDisableNative, chkDisableNative) = ctx.Rows.AddToggle(panel,
                "Disable native SSMS IntelliSense",
                "Recommended to avoid conflicts with AKML SQL IntelliSense", chkEnabled);
            ctx.RegisterSearch("Disable native SSMS IntelliSense", "Recommended to avoid conflicts with AKML SQL IntelliSense", "Toggle", rowDisableNative);

            // Spec 030 T080 (FR-043) — special-character handling (bracket / parenthesis /
            // auto-close) consolidated onto the Inserted Code › Special characters page.

            // Spec 030 T078 (FR-042) — completion commit keys.
            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Commit keys");

            var (rowSpaceCommit, chkSpaceCommit) = ctx.Rows.AddToggle(panel,
                "Commit with Space",
                "Press Space to commit the highlighted completion item. Off by default — most SSMS users expect Space to insert a literal space", chkEnabled);
            ctx.RegisterSearch("Commit with Space", "Press Space to commit the highlighted completion item", "Toggle", rowSpaceCommit);

            var (rowDotCommit, chkDotCommit) = ctx.Rows.AddToggle(panel,
                "Commit with Dot",
                "Press '.' to commit the highlighted completion item and continue with a member access", chkEnabled);
            ctx.RegisterSearch("Commit with Dot", "Press '.' to commit the highlighted completion item and continue with a member access", "Toggle", rowDotCommit);

            var (rowSnippets, chkSnippets) = ctx.Rows.AddToggle(panel,
                "Show snippets in the completion list",
                "Include snippet shortcuts (sel, ssf, ins, …) in the completion popup", chkEnabled);
            ctx.RegisterSearch("Show snippets in the completion list", "Include snippet shortcuts (sel, ssf, ins, …) in the completion popup", "Toggle", rowSnippets);

            // SQL-authentication credential settings moved to the Connections & Memory page
            // (SQL Prompt's "Connections & memory" pane).

            return new IntelliSenseControls(chkEnabled, chkAutoTrig, chkAfterDot, chkFuzzy,
                numMaxSugg, numTrigDelay,
                chkDataTypes, chkNullable, chkPkFk, chkCtrlTransparent,
                chkJoin, chkAlias, chkDisableNative,
                chkSpaceCommit, chkDotCommit, chkSnippets);
        }
    }

    internal sealed class IntelliSenseControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _autoTrigger;
        private readonly CheckBox _afterDot;
        private readonly CheckBox _fuzzyMatch;
        private readonly TextBox _maxSuggestions;
        private readonly TextBox _triggerDelay;
        private readonly CheckBox _showDataTypes;
        private readonly CheckBox _showNullability;
        private readonly CheckBox _showPkFk;
        private readonly CheckBox _ctrlTransparentPopups;
        private readonly CheckBox _joinAssist;
        private readonly CheckBox _autoAlias;
        private readonly CheckBox _disableNativeIs;
        private readonly CheckBox _spaceCommits;
        private readonly CheckBox _dotCommits;
        private readonly CheckBox _snippetsInCompletion;

        public IntelliSenseControls(CheckBox enabled, CheckBox autoTrig, CheckBox afterDot, CheckBox fuzzy,
            TextBox maxSuggestions, TextBox triggerDelay,
            CheckBox dataTypes, CheckBox nullable, CheckBox pkFk, CheckBox ctrlTransparentPopups,
            CheckBox join, CheckBox alias, CheckBox disableNative,
            CheckBox spaceCommits, CheckBox dotCommits, CheckBox snippetsInCompletion)
        {
            _enabled = enabled;
            _autoTrigger = autoTrig;
            _afterDot = afterDot;
            _fuzzyMatch = fuzzy;
            _maxSuggestions = maxSuggestions;
            _triggerDelay = triggerDelay;
            _showDataTypes = dataTypes;
            _showNullability = nullable;
            _showPkFk = pkFk;
            _ctrlTransparentPopups = ctrlTransparentPopups;
            _joinAssist = join;
            _autoAlias = alias;
            _disableNativeIs = disableNative;
            _spaceCommits = spaceCommits;
            _dotCommits = dotCommits;
            _snippetsInCompletion = snippetsInCompletion;
        }

        public void Load(AppSettings settings)
        {
            var i = settings.IntelliSense;
            _enabled.IsChecked = i.Enabled;
            _autoTrigger.IsChecked = i.AutoTrigger;
            _afterDot.IsChecked = i.AfterDot;
            _fuzzyMatch.IsChecked = i.FuzzyMatch;
            _showDataTypes.IsChecked = i.ShowDataTypes;
            _showNullability.IsChecked = i.ShowNullability;
            _showPkFk.IsChecked = i.ShowPkFk;
            _ctrlTransparentPopups.IsChecked = i.CtrlTransparentPopups;
            _autoAlias.IsChecked = i.AutoAlias;
            _joinAssist.IsChecked = i.JoinAssist;
            _disableNativeIs.IsChecked = i.DisableNativeIntelliSense;
            _spaceCommits.IsChecked = i.SpaceCommits;
            _dotCommits.IsChecked = i.DotCommits;
            _snippetsInCompletion.IsChecked = i.SnippetsInCompletion;
            RowFactory.SetNumber(_triggerDelay, i.TriggerDelayMs);
            RowFactory.SetNumber(_maxSuggestions, i.MaxSuggestions);
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.Enabled = _enabled.IsChecked == true;
            settings.IntelliSense.AutoTrigger = _autoTrigger.IsChecked == true;
            settings.IntelliSense.AfterDot = _afterDot.IsChecked == true;
            settings.IntelliSense.FuzzyMatch = _fuzzyMatch.IsChecked == true;
            settings.IntelliSense.ShowDataTypes = _showDataTypes.IsChecked == true;
            settings.IntelliSense.ShowNullability = _showNullability.IsChecked == true;
            settings.IntelliSense.ShowPkFk = _showPkFk.IsChecked == true;
            settings.IntelliSense.CtrlTransparentPopups = _ctrlTransparentPopups.IsChecked == true;
            settings.IntelliSense.AutoAlias = _autoAlias.IsChecked == true;
            settings.IntelliSense.JoinAssist = _joinAssist.IsChecked == true;
            settings.IntelliSense.DisableNativeIntelliSense = _disableNativeIs.IsChecked == true;
            settings.IntelliSense.SpaceCommits = _spaceCommits.IsChecked == true;
            settings.IntelliSense.DotCommits = _dotCommits.IsChecked == true;
            settings.IntelliSense.SnippetsInCompletion = _snippetsInCompletion.IsChecked == true;
            settings.IntelliSense.TriggerDelayMs = RowFactory.GetNumber(_triggerDelay);
            settings.IntelliSense.MaxSuggestions = RowFactory.GetNumber(_maxSuggestions);
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
