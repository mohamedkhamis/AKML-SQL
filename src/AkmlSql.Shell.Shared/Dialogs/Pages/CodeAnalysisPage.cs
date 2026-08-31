#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class CodeAnalysisPage : IPageBuilder
    {
        public string Key     => "Code Analysis";
        public string Display => "Code Analysis";
        public string Title   => "Code Analysis";
        public string Help    => "Controls the code analysis engine: enable it overall, choose whether rules run while you type or on save, and whether issues appear in the Error List.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Analysis Engine");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable code analysis",
                "Master switch for all 120+ analysis rules");
            ctx.RegisterSearch("Enable code analysis", "Master switch for all 120+ analysis rules", "Toggle", rowEnabled);

            var (rowRunOnType, chkRunOnType) = ctx.Rows.AddToggle(panel,
                "Analyze while typing",
                "Run analysis rules in real-time as you type");
            ctx.RegisterSearch("Analyze while typing", "Run analysis rules in real-time as you type", "Toggle", rowRunOnType);

            var (rowRunOnSave, chkRunOnSave) = ctx.Rows.AddToggle(panel,
                "Analyze on save",
                "Run full analysis when the document is saved");
            ctx.RegisterSearch("Analyze on save", "Run full analysis when the document is saved", "Toggle", rowRunOnSave);

            var (rowShowInErrorList, chkShowInErrorList) = ctx.Rows.AddToggle(panel,
                "Show in Error List",
                "Report analysis issues in the VS/SSMS Error List window");
            ctx.RegisterSearch("Show in Error List", "Report analysis issues in the VS/SSMS Error List window", "Toggle", rowShowInErrorList);

            ctx.Rows.AddGroupSeparator(panel);
            var rulesRow = ctx.Rows.AddInfoRow(panel, "Rules", "120+ rules across 8 categories (PE, BP, SE, ST, DE, DEP, EX, NM)");
            ctx.RegisterSearch("Rules", "120+ rules across 8 categories", "Info", rulesRow);
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

            return new CodeAnalysisControls(chkEnabled, chkRunOnType, chkRunOnSave, chkShowInErrorList);
        }
    }

    internal sealed class CodeAnalysisControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _runOnType;
        private readonly CheckBox _runOnSave;
        private readonly CheckBox _showInErrorList;

        public CodeAnalysisControls(CheckBox enabled, CheckBox runOnType, CheckBox runOnSave, CheckBox showInErrorList)
        {
            _enabled = enabled;
            _runOnType = runOnType;
            _runOnSave = runOnSave;
            _showInErrorList = showInErrorList;
        }

        public void Load(AppSettings settings)
        {
            var ca = settings.CodeAnalysis;
            _enabled.IsChecked = ca.Enabled;
            _runOnType.IsChecked = ca.RunOnType;
            _runOnSave.IsChecked = ca.RunOnSave;
            _showInErrorList.IsChecked = ca.ShowInErrorList;
        }

        public void Save(AppSettings settings)
        {
            settings.CodeAnalysis.Enabled = _enabled.IsChecked == true;
            settings.CodeAnalysis.RunOnType = _runOnType.IsChecked == true;
            settings.CodeAnalysis.RunOnSave = _runOnSave.IsChecked == true;
            settings.CodeAnalysis.ShowInErrorList = _showInErrorList.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
