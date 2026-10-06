#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Spec 030 T078 (FR-042 / SC-007) — Suggestions › Tooltips. Spec 040 (OPT-01): only the
    /// object definition box is shown; the other <see cref="CompletionPolishSettings"/> rows
    /// (descriptions, parameter highlight, decryption, temp tables, Column Picker sort) changed
    /// nothing and are hidden until they work. Their saved values are left untouched.
    /// </summary>
    internal sealed class CompletionPolishPage : IPageBuilder
    {
        public string Key     => "CompletionPolish";
        public string Display => "Suggestions › Behavior › Tooltips";
        public string Title   => "Tooltips";
        public string HelpTopic => "topics/options#tooltips";
        public string Help    => "Whether the definition panel (columns, details and script) appears beside the suggestions box when an object is selected.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Tooltips");

            var (rowDefBox, chkDefBox) = ctx.Rows.AddToggle(panel,
                "Show the object definition box",
                "Show the definition panel (columns, details, script) beside the completion popup when an item is selected");
            ctx.RegisterSearch("Show the object definition box", "Show the definition panel beside the completion popup when an item is selected", "Toggle", rowDefBox);

            return new CompletionPolishControls(chkDefBox);
        }
    }

    internal sealed class CompletionPolishControls : IPageControls
    {
        private readonly CheckBox _showDefinitionBox;

        public CompletionPolishControls(CheckBox showDefinitionBox)
        {
            _showDefinitionBox = showDefinitionBox;
        }

        public void Load(AppSettings settings)
        {
            _showDefinitionBox.IsChecked = settings.CompletionPolish.ShowObjectDefinitionBox;
        }

        public void Save(AppSettings settings)
        {
            settings.CompletionPolish.ShowObjectDefinitionBox = _showDefinitionBox.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
