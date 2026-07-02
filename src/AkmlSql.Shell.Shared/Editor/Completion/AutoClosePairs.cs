#nullable enable
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// Pure decision logic for the auto-close-characters feature (SQL Prompt's "Automatically
    /// insert the corresponding closing character"): given the character just typed and its
    /// immediate neighbours, decides which closing text (if any) to insert at the caret.
    /// Kept free of editor types so the pairing rules and guards are unit-testable; the
    /// buffer edit and caret handling live in <c>CompletionController.HandleAutoClose</c>.
    /// </summary>
    internal static class AutoClosePairs
    {
        /// <summary>
        /// Returns the closing text to insert after the caret, or null when nothing should be
        /// inserted. <paramref name="prev"/> is the character before the one just typed,
        /// <paramref name="prev2"/> the one before that, and <paramref name="next"/> the character
        /// after the caret ('\0' at buffer edges).
        /// </summary>
        public static string? TryGetCloser(char typed, char prev, char prev2, char next, SpecialCharacterSettings settings)
        {
            if (!settings.AutoCloseCharacters) return null;

            switch (typed)
            {
                case '(':
                    return settings.CloseParenthesis && !IsIdentifierChar(next) ? ")" : null;
                case '[':
                    return settings.CloseSquareBracket && !IsIdentifierChar(next) ? "]" : null;
                case '\'':
                    // Word guard on BOTH sides: the apostrophe in "don't" (prev = letter) and a
                    // quote typed directly before a word must both stay a lone quote. A doubled
                    // quote (prev/next already a quote) is a T-SQL escape — leave it alone too.
                    // Carve-out: the T-SQL Unicode-literal prefix N'…' (either case) auto-closes
                    // when the N starts a word (prev2 is not a word char) — "= N'" closes, the
                    // trailing n of "don'" / "in'" does not.
                    if (!settings.CloseSingleQuote || IsWordOrQuote(next, '\'')) return null;
                    if (IsWordOrQuote(prev, '\'')
                        && !(char.ToUpperInvariant(prev) == 'N' && !IsWordOrQuote(prev2, '\'')))
                        return null;
                    return "'";
                case '"':
                    return settings.CloseDoubleQuote && !IsWordOrQuote(prev, '"') && !IsWordOrQuote(next, '"')
                        ? "\"" : null;
                case '*':
                    // Only the comment opener /* pairs; a bare * is multiplication or a wildcard.
                    // Skip when the next char is * or / — the caret is already inside comment chrome.
                    return settings.CloseCommentMark && prev == '/' && next != '*' && next != '/'
                        ? "*/" : null;
                default:
                    return null;
            }
        }

        // Matches CompletionController.IsIdentifierChar so auto-close and completion-span logic
        // agree on word boundaries at the same caret position.
        private static bool IsIdentifierChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#';

        private static bool IsWordOrQuote(char c, char quote)
            => IsIdentifierChar(c) || c == quote;
    }
}
