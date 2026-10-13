#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Suggestions › Behavior › Completion (the 2026-10 "rebalanced tree", design B): the
    /// completion switches in fitted rows — one short line under a row only where the label
    /// does not say it all (the old explanation stays as the row's tooltip), runs of toggles in
    /// two columns, the two number fields on one line — plus the object definition box, once a
    /// one-row "Tooltips" page. The JOIN rows moved to <see cref="JoinAssistancePage"/>, the
    /// page's second tab, still greyed by Enable IntelliSense.
    /// </summary>
    internal sealed class IntelliSensePage : IPageBuilder
    {
        public string Key     => "IntelliSense";
        public string Display => "Suggestions › Behavior › Completion";
        public string Title   => "Completion";
        public string HelpTopic => "topics/options#completion";
        public string Help    => "Controls AKML SQL completion behavior — auto-triggering, fuzzy matching, suggestion count and trigger delay, column/PK/FK details, popup Ctrl-transparency, the definition box, commit keys and snippets. JOIN assistance is the next tab; special characters live on Inserted code; SQL-auth credentials on General.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Completion");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable IntelliSense", "Master switch");
            ctx.RegisterSearch("Enable IntelliSense", "Master switch for all IntelliSense features", "Toggle", rowEnabled);

            var (coreLeft, coreRight) = ctx.Rows.AddColumns(panel);

            var (rowAutoTrig, chkAutoTrig) = ctx.Rows.AddToggle(coreLeft,
                "Auto-trigger completions while typing", "", chkEnabled);
            ctx.Rows.Tip(rowAutoTrig, "Show the completion list automatically, without Ctrl+Space");
            ctx.RegisterSearch("Auto-trigger completions while typing", "Show completion list automatically without Ctrl+Space", "Toggle", rowAutoTrig);

            var (rowAfterDot, chkAfterDot) = ctx.Rows.AddToggle(coreRight,
                "Trigger after dot", "", chkEnabled);
            ctx.Rows.Tip(rowAfterDot, "Auto-complete after typing '.' for table.column references");
            ctx.RegisterSearch("Trigger after dot", "Auto-complete after typing '.' for table.column references", "Toggle", rowAfterDot);

            var (rowFuzzy, chkFuzzy) = ctx.Rows.AddToggle(coreLeft,
                "Enable fuzzy matching", "", chkEnabled);
            ctx.Rows.Tip(rowFuzzy, "Substring and approximate matching in addition to prefix");
            ctx.RegisterSearch("Enable fuzzy matching", "Substring and approximate matching in addition to prefix", "Toggle", rowFuzzy);

            var (rowDisableNative, chkDisableNative) = ctx.Rows.AddToggle(coreRight,
                "Disable native SSMS IntelliSense", "Recommended; avoids two completion lists", chkEnabled);
            ctx.RegisterSearch("Disable native SSMS IntelliSense", "Recommended to avoid conflicts with AKML SQL IntelliSense", "Toggle", rowDisableNative);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Completion list");

            // Spec 040 (OPT-06): number fields, not sliders, for wide ranges.
            var (numLeft, numRight) = ctx.Rows.AddColumns(panel);
            var (rowMaxSugg, numMaxSugg) = ctx.Rows.AddNumber(numLeft,
                "Maximum suggestions", 5, 200, 5, "items", "", chkEnabled);
            ctx.Rows.Tip(rowMaxSugg, "Maximum number of items shown in the completion list");
            ctx.RegisterSearch("Maximum suggestions", "Maximum number of items shown in the completion list", "Number", rowMaxSugg);

            var (rowTrigDelay, numTrigDelay) = ctx.Rows.AddNumber(numRight,
                "Trigger delay", 0, 2000, 50, "ms", "", chkEnabled);
            ctx.Rows.Tip(rowTrigDelay, "Debounce delay before showing completions");
            ctx.RegisterSearch("Trigger delay", "Debounce delay before showing completions", "Number", rowTrigDelay);

            // Spec 040 (OPT-01): "Keyword casing" is hidden — nothing reads it; the saved value is kept.

            var (listLeft, listRight) = ctx.Rows.AddColumns(panel);

            var (rowDataTypes, chkDataTypes) = ctx.Rows.AddToggle(listLeft,
                "Show column data types", "", chkEnabled);
            ctx.Rows.Tip(rowDataTypes, "Display data type information in completion details");
            ctx.RegisterSearch("Show column data types", "Display data type information in completion details", "Toggle", rowDataTypes);

            var (rowNullable, chkNullable) = ctx.Rows.AddToggle(listRight,
                "Show nullability info", "", chkEnabled);
            ctx.Rows.Tip(rowNullable, "Show NOT NULL / NULL status in completion details");
            ctx.RegisterSearch("Show nullability info", "Show NOT NULL / NULL status in completion details", "Toggle", rowNullable);

            var (rowPkFk, chkPkFk) = ctx.Rows.AddToggle(listLeft,
                "Show PK/FK indicators", "", chkEnabled);
            ctx.Rows.Tip(rowPkFk, "Show primary key and foreign key badges");
            ctx.RegisterSearch("Show PK/FK indicators", "Show primary key and foreign key badges", "Toggle", rowPkFk);

            var (rowCtrlTransparent, chkCtrlTransparent) = ctx.Rows.AddToggle(listRight,
                "Make popups transparent when Ctrl is held", "", chkEnabled);
            ctx.Rows.Tip(rowCtrlTransparent, "Hold Ctrl to see the code underneath the completion popup");
            ctx.RegisterSearch("Make popups transparent when Ctrl is held", "Hold Ctrl to see the code underneath the completion popup", "Toggle", rowCtrlTransparent);

            var (rowSnippets, chkSnippets) = ctx.Rows.AddToggle(listLeft,
                "Show snippets in the completion list", "", chkEnabled);
            ctx.Rows.Tip(rowSnippets, "Include snippet shortcuts (sel, ssf, ins, …) in the completion popup");
            ctx.RegisterSearch("Show snippets in the completion list", "Include snippet shortcuts (sel, ssf, ins, …) in the completion popup", "Toggle", rowSnippets);

            // Once the one-row "Tooltips" page (spec 030 T078): the definition panel beside the popup.
            var (rowDefBox, chkDefBox) = ctx.Rows.AddToggle(listRight,
                "Show the object definition box", "Columns, details and script beside the popup", chkEnabled);
            ctx.RegisterSearch("Show the object definition box", "Show the definition panel beside the completion popup when an item is selected", "Toggle", rowDefBox);

            // Spec 030 T078 (FR-042) — completion commit keys.
            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Commit keys");

            var (keysLeft, keysRight) = ctx.Rows.AddColumns(panel);

            var (rowSpaceCommit, chkSpaceCommit) = ctx.Rows.AddToggle(keysLeft,
                "Commit with Space", "Off by default: Space inserts a space", chkEnabled);
            ctx.Rows.Tip(rowSpaceCommit, "Press Space to commit the highlighted completion item. Off by default — most SSMS users expect Space to insert a literal space");
            ctx.RegisterSearch("Commit with Space", "Press Space to commit the highlighted completion item", "Toggle", rowSpaceCommit);

            var (rowDotCommit, chkDotCommit) = ctx.Rows.AddToggle(keysRight,
                "Commit with Dot", "Commit and continue with a member access", chkEnabled);
            ctx.Rows.Tip(rowDotCommit, "Press '.' to commit the highlighted completion item and continue with a member access");
            ctx.RegisterSearch("Commit with Dot", "Press '.' to commit the highlighted completion item and continue with a member access", "Toggle", rowDotCommit);

            return new IntelliSenseControls(chkEnabled, chkAutoTrig, chkAfterDot, chkFuzzy,
                numMaxSugg, numTrigDelay,
                chkDataTypes, chkNullable, chkPkFk, chkCtrlTransparent,
                chkDisableNative, chkSpaceCommit, chkDotCommit, chkSnippets, chkDefBox);
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
        private readonly CheckBox _disableNativeIs;
        private readonly CheckBox _spaceCommits;
        private readonly CheckBox _dotCommits;
        private readonly CheckBox _snippetsInCompletion;
        private readonly CheckBox _showDefinitionBox;

        public IntelliSenseControls(CheckBox enabled, CheckBox autoTrig, CheckBox afterDot, CheckBox fuzzy,
            TextBox maxSuggestions, TextBox triggerDelay,
            CheckBox dataTypes, CheckBox nullable, CheckBox pkFk, CheckBox ctrlTransparentPopups,
            CheckBox disableNative, CheckBox spaceCommits, CheckBox dotCommits, CheckBox snippetsInCompletion,
            CheckBox showDefinitionBox)
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
            _disableNativeIs = disableNative;
            _spaceCommits = spaceCommits;
            _dotCommits = dotCommits;
            _snippetsInCompletion = snippetsInCompletion;
            _showDefinitionBox = showDefinitionBox;
        }

        /// <summary>The master switch — the JOIN assistance tab's rows grey with it (test seam).</summary>
        internal CheckBox Enabled => _enabled;

        /// <summary>The object definition box toggle (test seam).</summary>
        internal CheckBox ShowDefinitionBox => _showDefinitionBox;

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
            _disableNativeIs.IsChecked = i.DisableNativeIntelliSense;
            _spaceCommits.IsChecked = i.SpaceCommits;
            _dotCommits.IsChecked = i.DotCommits;
            _snippetsInCompletion.IsChecked = i.SnippetsInCompletion;
            RowFactory.SetNumber(_triggerDelay, i.TriggerDelayMs);
            RowFactory.SetNumber(_maxSuggestions, i.MaxSuggestions);
            _showDefinitionBox.IsChecked = settings.CompletionPolish.ShowObjectDefinitionBox;
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
            settings.IntelliSense.DisableNativeIntelliSense = _disableNativeIs.IsChecked == true;
            settings.IntelliSense.SpaceCommits = _spaceCommits.IsChecked == true;
            settings.IntelliSense.DotCommits = _dotCommits.IsChecked == true;
            settings.IntelliSense.SnippetsInCompletion = _snippetsInCompletion.IsChecked == true;
            settings.IntelliSense.TriggerDelayMs = RowFactory.GetNumber(_triggerDelay);
            settings.IntelliSense.MaxSuggestions = RowFactory.GetNumber(_maxSuggestions);
            settings.CompletionPolish.ShowObjectDefinitionBox = _showDefinitionBox.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
