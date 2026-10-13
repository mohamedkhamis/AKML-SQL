#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Inserted code › Closing characters (the 2026-10 "rebalanced tree", design B): the
    /// auto-close master switch and its five per-character toggles
    /// (<see cref="SpecialCharacterSettings"/>), in two columns. The bracket policy and the
    /// function parentheses this section once held (spec 030's single "Special characters"
    /// pane) sit with schema qualification on the Statements tab now. The key stays
    /// <c>SpecialCharacters</c> so links, search and F1 keep resolving.
    /// </summary>
    internal sealed class SpecialCharactersPage : IPageBuilder
    {
        public string Key     => "SpecialCharacters";
        public string Display => "Inserted code › Closing characters";
        public string Title   => "Closing characters";
        public string HelpTopic => "topics/options#special-characters";
        public string Help    => "Whether typing an opening quote, comment mark, parenthesis or bracket inserts its closing character after the caret, and which of the five it applies to.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Closing characters");

            var (rowAutoClose, chkAutoClose) = ctx.Rows.AddToggle(panel,
                "Automatically insert the corresponding closing character",
                "Master switch for the characters below");
            ctx.RegisterSearch("Automatically insert the corresponding closing character", "Master switch for auto-close characters", "Toggle", rowAutoClose);

            var (left, right) = ctx.Rows.AddColumns(panel);

            var (rowSingle, chkSingle) = ctx.Rows.AddToggle(left, "Single quotation mark ( ' )", "", chkAutoClose);
            ctx.Rows.Tip(rowSingle, "Typing ' inserts the closing ' after the caret");
            ctx.RegisterSearch("Single quotation mark ( ' )", "Auto-close single quotation marks", "Toggle", rowSingle);

            var (rowDouble, chkDouble) = ctx.Rows.AddToggle(right, "Double quotation mark ( \" )", "", chkAutoClose);
            ctx.Rows.Tip(rowDouble, "Typing \" inserts the closing \" after the caret");
            ctx.RegisterSearch("Double quotation mark ( \" )", "Auto-close double quotation marks", "Toggle", rowDouble);

            var (rowComment, chkComment) = ctx.Rows.AddToggle(left, "Comment mark ( */ )", "", chkAutoClose);
            ctx.Rows.Tip(rowComment, "Typing /* inserts the closing */ after the caret");
            ctx.RegisterSearch("Comment mark ( */ )", "Auto-close block comment marks", "Toggle", rowComment);

            var (rowParen, chkParen) = ctx.Rows.AddToggle(right, "Parenthesis )", "", chkAutoClose);
            ctx.Rows.Tip(rowParen, "Typing ( inserts the closing ) after the caret");
            ctx.RegisterSearch("Parenthesis )", "Auto-close parentheses", "Toggle", rowParen);

            var (rowSquare, chkSquare) = ctx.Rows.AddToggle(left, "Square bracket ]", "", chkAutoClose);
            ctx.Rows.Tip(rowSquare, "Typing [ inserts the closing ] after the caret");
            ctx.RegisterSearch("Square bracket ]", "Auto-close square brackets", "Toggle", rowSquare);

            return new SpecialCharactersControls(chkAutoClose, chkSingle, chkDouble, chkComment, chkParen, chkSquare);
        }
    }

    internal sealed class SpecialCharactersControls : IPageControls
    {
        public SpecialCharactersControls(CheckBox autoCloseChars,
            CheckBox closeSingleQuote, CheckBox closeDoubleQuote, CheckBox closeCommentMark,
            CheckBox closeParenthesis, CheckBox closeSquareBracket)
        {
            AutoCloseCharacters = autoCloseChars;
            CloseSingleQuote = closeSingleQuote;
            CloseDoubleQuote = closeDoubleQuote;
            CloseCommentMark = closeCommentMark;
            CloseParenthesis = closeParenthesis;
            CloseSquareBracket = closeSquareBracket;
        }

        internal CheckBox AutoCloseCharacters { get; }
        internal CheckBox CloseSingleQuote { get; }
        internal CheckBox CloseDoubleQuote { get; }
        internal CheckBox CloseCommentMark { get; }
        internal CheckBox CloseParenthesis { get; }
        internal CheckBox CloseSquareBracket { get; }

        public void Load(AppSettings settings)
        {
            var sc = settings.IntelliSense.SpecialCharOptions;
            AutoCloseCharacters.IsChecked = sc.AutoCloseCharacters;
            CloseSingleQuote.IsChecked = sc.CloseSingleQuote;
            CloseDoubleQuote.IsChecked = sc.CloseDoubleQuote;
            CloseCommentMark.IsChecked = sc.CloseCommentMark;
            CloseParenthesis.IsChecked = sc.CloseParenthesis;
            CloseSquareBracket.IsChecked = sc.CloseSquareBracket;
        }

        public void Save(AppSettings settings)
        {
            var sc = settings.IntelliSense.SpecialCharOptions;
            sc.AutoCloseCharacters = AutoCloseCharacters.IsChecked == true;
            sc.CloseSingleQuote = CloseSingleQuote.IsChecked == true;
            sc.CloseDoubleQuote = CloseDoubleQuote.IsChecked == true;
            sc.CloseCommentMark = CloseCommentMark.IsChecked == true;
            sc.CloseParenthesis = CloseParenthesis.IsChecked == true;
            sc.CloseSquareBracket = CloseSquareBracket.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
