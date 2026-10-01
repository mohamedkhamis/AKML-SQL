#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class CodeAnalysisPage : IPageBuilder
    {
        public string Key     => "Code Analysis";
        public string Display => "Code analysis";
        public string Title   => "Code analysis";
        public string HelpTopic => "topics/options#code-analysis";
        public string Help    => "Controls the code analysis engine: enable it overall, choose whether rules run while you type, and whether issues appear in the Error List.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Analysis engine");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable code analysis",
                "Master switch for all 120+ analysis rules");
            ctx.RegisterSearch("Enable code analysis", "Master switch for all 120+ analysis rules", "Toggle", rowEnabled);

            var (rowRunOnType, chkRunOnType) = ctx.Rows.AddToggle(panel,
                "Analyze while typing",
                "Run analysis rules in real-time as you type", chkEnabled);
            ctx.RegisterSearch("Analyze while typing", "Run analysis rules in real-time as you type", "Toggle", rowRunOnType);

            // Spec 040 (OPT-01): "Analyze on save" changed nothing and is hidden; the saved value is kept.

            var (rowShowInErrorList, chkShowInErrorList) = ctx.Rows.AddToggle(panel,
                "Show in Error List",
                "Report analysis issues in the VS/SSMS Error List window", chkEnabled);
            ctx.RegisterSearch("Show in Error List", "Report analysis issues in the VS/SSMS Error List window", "Toggle", rowShowInErrorList);

            ctx.Rows.AddGroupSeparator(panel);
            // Spec 040 (T168, OPT-09) — the "Rules" row opens the Code analysis rules window. The
            // window saves the overrides itself; the page then takes them from disk (see
            // CodeAnalysisControls.RefreshRuleOverridesFromDisk) so OK here can't write stale ones.
            const string rulesHint = "120+ rules across 8 categories (PE, BP, SE, ST, DE, DEP, EX, NM): turn each on or off and set its severity";
            var (rulesRow, btnManageRules) = ctx.Rows.AddButton(panel, "Rules", "Manage rules…", rulesHint);
            ctx.RegisterSearch("Rules", rulesHint, "Button", rulesRow);
            var perProjectRow = ctx.Rows.AddInfoRow(panel, "Per-project config", ".casettings JSON file searched upward from file");
            ctx.RegisterSearch("Per-project config", ".casettings JSON file searched upward from file", "Info", perProjectRow);
            const string suppressHint =
                "-- akml-disable-line RuleId (one line) · -- akml-disable RuleId … -- akml-enable RuleId " +
                "(a block; omit the enable to cover the whole script)";
            var suppressRow = ctx.Rows.AddInfoRow(panel, "Inline suppression", suppressHint);
            ctx.RegisterSearch("Inline suppression", suppressHint, "Info", suppressRow);

            const string scopeHint =
                "Click the warning glyph or lightbulb: this line · this script · this session · everywhere";
            var scopeRow = ctx.Rows.AddInfoRow(panel, "Disable a rule", scopeHint);
            ctx.RegisterSearch("Disable a rule", scopeHint, "Info", scopeRow);

            var controls = new CodeAnalysisControls(chkEnabled, chkRunOnType, chkShowInErrorList);
            btnManageRules.Click += (_, _) =>
            {
                Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                Analysis.ManageRulesCommand.Open(System.Windows.Window.GetWindow(btnManageRules));
                controls.RefreshRuleOverridesFromDisk();
            };
            return controls;
        }
    }

    internal sealed class CodeAnalysisControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _runOnType;
        private readonly CheckBox _showInErrorList;

        public CodeAnalysisControls(CheckBox enabled, CheckBox runOnType, CheckBox showInErrorList)
        {
            _enabled = enabled;
            _runOnType = runOnType;
            _showInErrorList = showInErrorList;
        }

        // Spec 040 (T168) — the rule overrides "Manage rules…" left on disk. Options' working copy
        // was loaded before the rules window saved, so Save writes these instead of the stale
        // copy. Load drops them: it runs when the working copy is replaced (window open, Import,
        // Restore all defaults), and that replacement wins.
        private System.Collections.Generic.Dictionary<string, RuleOverride>? _ruleOverridesFromDisk;

        public void Load(AppSettings settings)
        {
            var ca = settings.CodeAnalysis;
            _enabled.IsChecked = ca.Enabled;
            _runOnType.IsChecked = ca.RunOnType;
            _showInErrorList.IsChecked = ca.ShowInErrorList;
            _ruleOverridesFromDisk = null;
        }

        public void Save(AppSettings settings)
        {
            settings.CodeAnalysis.Enabled = _enabled.IsChecked == true;
            settings.CodeAnalysis.RunOnType = _runOnType.IsChecked == true;
            settings.CodeAnalysis.ShowInErrorList = _showInErrorList.IsChecked == true;
            if (_ruleOverridesFromDisk != null)
                settings.CodeAnalysis.RuleOverrides = _ruleOverridesFromDisk; // the setter copies
        }

        // A page reset keeps the rule overrides (the page doesn't show them), including any that
        // "Manage rules…" just saved.
        public void Reset(AppSettings defaults)
        {
            var fromDisk = _ruleOverridesFromDisk;
            Load(defaults);
            _ruleOverridesFromDisk = fromDisk;
        }

        /// <summary>
        /// Spec 040 (T168) — called after the Code analysis rules window closes: takes the rule
        /// overrides from <c>config.json</c> so the next Save (OK / Apply) writes them, not the
        /// copy Options loaded when it opened. Same pattern as
        /// <see cref="FormattingControls.RefreshActiveStyleFromDisk"/>.
        /// </summary>
        internal void RefreshRuleOverridesFromDisk()
        {
            try
            {
                _ruleOverridesFromDisk = ConfigManager.Load().CodeAnalysis.RuleOverrides;
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Warning(ex, "CodeAnalysisPage: rule-override refresh failed");
            }
        }
    }
}
