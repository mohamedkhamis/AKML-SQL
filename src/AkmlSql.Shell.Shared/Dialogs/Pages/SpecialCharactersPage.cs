#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Inserted Code › Special characters. Consolidates SQL Prompt's single "Special
    /// characters" pane: bracket-identifier policy (from
    /// <see cref="QualificationSettings.BracketMode"/>), auto-add parentheses after
    /// functions, and auto-close matching characters (both from
    /// <see cref="SpecialCharacterSettings"/>). These were previously scattered across the
    /// IntelliSense (Behavior) and Qualification pages; SQL Prompt keeps them together, so
    /// this page owns all three (report §4 rec #1). The old pages no longer touch them.
    /// </summary>
    internal sealed class SpecialCharactersPage : IPageBuilder
    {
        public string Key     => "SpecialCharacters";
        public string Display => "Inserted Code › Special characters";
        public string Title   => "Special characters";
        public string Help    => "Controls the special characters AKML SQL inserts as you type and complete: when identifiers are wrapped in [square brackets], whether parentheses are added after a function is committed, and whether typing an opening bracket, brace, or quote inserts its matching closing character.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Brackets");

            var (rowBracket, cboBracket) = ctx.Rows.AddDropdown(panel,
                "Bracket identifiers",
                new[] { "Always", "When required", "Never" },
                "When to wrap inserted identifiers in [square brackets]: always, only when needed (reserved words / spaces), or never.");
            ctx.RegisterSearch("Bracket identifiers", "Bracket policy for inserted identifiers", "Dropdown", rowBracket);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Functions & closing characters");

            var (rowAddParens, chkAddParens) = ctx.Rows.AddToggle(panel,
                "Add parentheses after functions",
                "Automatically add parentheses when a function is inserted from the completion list");
            ctx.RegisterSearch("Add parentheses after functions", "Automatically add parentheses when a function is inserted from the completion list", "Toggle", rowAddParens);

            var (rowAutoClose, chkAutoClose) = ctx.Rows.AddToggle(panel,
                "Auto-close matching characters",
                "Typing an opening bracket, brace, or quote inserts the matching closing character");
            ctx.RegisterSearch("Auto-close matching characters", "Typing an opening bracket, brace, or quote inserts the matching closing character", "Toggle", rowAutoClose);

            return new SpecialCharactersControls(cboBracket, chkAddParens, chkAutoClose);
        }
    }

    internal sealed class SpecialCharactersControls : IPageControls
    {
        private readonly ComboBox _bracketMode;
        private readonly CheckBox _addParentheses;
        private readonly CheckBox _autoCloseChars;

        public SpecialCharactersControls(ComboBox bracketMode, CheckBox addParentheses, CheckBox autoCloseChars)
        {
            _bracketMode = bracketMode;
            _addParentheses = addParentheses;
            _autoCloseChars = autoCloseChars;
        }

        public void Load(AppSettings settings)
        {
            _bracketMode.SelectedIndex = settings.IntelliSense.Qualification.BracketMode switch
            {
                BracketMode.Always       => 0,
                BracketMode.WhenRequired => 1,
                BracketMode.Never        => 2,
                _ => 1,
            };
            _addParentheses.IsChecked = settings.IntelliSense.SpecialCharOptions.AddParentheses;
            _autoCloseChars.IsChecked = settings.IntelliSense.SpecialCharOptions.AutoCloseCharacters;
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.Qualification.BracketMode = _bracketMode.SelectedIndex switch
            {
                0 => BracketMode.Always,
                2 => BracketMode.Never,
                _ => BracketMode.WhenRequired,
            };
            settings.IntelliSense.SpecialCharOptions.AddParentheses = _addParentheses.IsChecked == true;
            settings.IntelliSense.SpecialCharOptions.AutoCloseCharacters = _autoCloseChars.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
