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
            ctx.Rows.AddGroupHeader(panel, "Functions & data types");

            var (rowAddParens, chkAddParens) = ctx.Rows.AddToggle(panel,
                "Add parentheses ( ) when inserting a function or data type",
                "Automatically add parentheses when a function or parameterized data type is inserted from the completion list");
            ctx.RegisterSearch("Add parentheses ( ) when inserting a function or data type", "Automatically add parentheses when a function or parameterized data type is inserted from the completion list", "Toggle", rowAddParens);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Closing characters");

            var (rowAutoClose, chkAutoClose) = ctx.Rows.AddToggle(panel,
                "Automatically insert the corresponding closing character",
                "Master switch for the per-character toggles below");
            ctx.RegisterSearch("Automatically insert the corresponding closing character", "Master switch for auto-close characters", "Toggle", rowAutoClose);

            var (rowSingle, chkSingle) = ctx.Rows.AddToggle(panel,
                "Single quotation mark ( ' )",
                "Typing ' inserts the closing ' after the caret");
            ctx.RegisterSearch("Single quotation mark ( ' )", "Auto-close single quotation marks", "Toggle", rowSingle);

            var (rowDouble, chkDouble) = ctx.Rows.AddToggle(panel,
                "Double quotation mark ( \" )",
                "Typing \" inserts the closing \" after the caret");
            ctx.RegisterSearch("Double quotation mark ( \" )", "Auto-close double quotation marks", "Toggle", rowDouble);

            var (rowComment, chkComment) = ctx.Rows.AddToggle(panel,
                "Comment mark ( */ )",
                "Typing /* inserts the closing */ after the caret");
            ctx.RegisterSearch("Comment mark ( */ )", "Auto-close block comment marks", "Toggle", rowComment);

            var (rowParen, chkParen) = ctx.Rows.AddToggle(panel,
                "Parenthesis )",
                "Typing ( inserts the closing ) after the caret");
            ctx.RegisterSearch("Parenthesis )", "Auto-close parentheses", "Toggle", rowParen);

            var (rowSquare, chkSquare) = ctx.Rows.AddToggle(panel,
                "Square bracket ]",
                "Typing [ inserts the closing ] after the caret");
            ctx.RegisterSearch("Square bracket ]", "Auto-close square brackets", "Toggle", rowSquare);

            return new SpecialCharactersControls(cboBracket, chkAddParens, chkAutoClose,
                chkSingle, chkDouble, chkComment, chkParen, chkSquare);
        }
    }

    internal sealed class SpecialCharactersControls : IPageControls
    {
        private readonly ComboBox _bracketMode;
        private readonly CheckBox _addParentheses;
        private readonly CheckBox _autoCloseChars;
        private readonly CheckBox _closeSingleQuote;
        private readonly CheckBox _closeDoubleQuote;
        private readonly CheckBox _closeCommentMark;
        private readonly CheckBox _closeParenthesis;
        private readonly CheckBox _closeSquareBracket;

        public SpecialCharactersControls(ComboBox bracketMode, CheckBox addParentheses, CheckBox autoCloseChars,
            CheckBox closeSingleQuote, CheckBox closeDoubleQuote, CheckBox closeCommentMark,
            CheckBox closeParenthesis, CheckBox closeSquareBracket)
        {
            _bracketMode = bracketMode;
            _addParentheses = addParentheses;
            _autoCloseChars = autoCloseChars;
            _closeSingleQuote = closeSingleQuote;
            _closeDoubleQuote = closeDoubleQuote;
            _closeCommentMark = closeCommentMark;
            _closeParenthesis = closeParenthesis;
            _closeSquareBracket = closeSquareBracket;
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
            var sc = settings.IntelliSense.SpecialCharOptions;
            _addParentheses.IsChecked = sc.AddParentheses;
            _autoCloseChars.IsChecked = sc.AutoCloseCharacters;
            _closeSingleQuote.IsChecked = sc.CloseSingleQuote;
            _closeDoubleQuote.IsChecked = sc.CloseDoubleQuote;
            _closeCommentMark.IsChecked = sc.CloseCommentMark;
            _closeParenthesis.IsChecked = sc.CloseParenthesis;
            _closeSquareBracket.IsChecked = sc.CloseSquareBracket;
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.Qualification.BracketMode = _bracketMode.SelectedIndex switch
            {
                0 => BracketMode.Always,
                2 => BracketMode.Never,
                _ => BracketMode.WhenRequired,
            };
            var sc = settings.IntelliSense.SpecialCharOptions;
            sc.AddParentheses = _addParentheses.IsChecked == true;
            sc.AutoCloseCharacters = _autoCloseChars.IsChecked == true;
            sc.CloseSingleQuote = _closeSingleQuote.IsChecked == true;
            sc.CloseDoubleQuote = _closeDoubleQuote.IsChecked == true;
            sc.CloseCommentMark = _closeCommentMark.IsChecked == true;
            sc.CloseParenthesis = _closeParenthesis.IsChecked == true;
            sc.CloseSquareBracket = _closeSquareBracket.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
