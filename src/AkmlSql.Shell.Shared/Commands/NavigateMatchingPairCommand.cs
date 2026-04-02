#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Navigates to the matching T-SQL block keyword or parenthesis at the caret (Ctrl+]).
    /// Supported pairs: BEGIN/END, TRY/CATCH, CASE/END, and parentheses ( / ).
    /// Uses a stack-based scanner to find the matching counterpart.
    /// </summary>
    internal sealed class NavigateMatchingPairCommand
    {
        // Opening keywords map to their closers; direction = forward
        private static readonly Dictionary<string, string> OpenToClose = new(StringComparer.OrdinalIgnoreCase)
        {
            { "BEGIN", "END" },
            { "CASE", "END" },
            { "(", ")" }
        };

        // Closing keywords map to their openers; direction = backward
        private static readonly Dictionary<string, string> CloseToOpen = new(StringComparer.OrdinalIgnoreCase)
        {
            { "END", "BEGIN" },   // also matches CASE but BEGIN is the default
            { ")", "(" }
        };

        // TRY/CATCH are special: BEGIN TRY -> END TRY, BEGIN CATCH -> END CATCH
        // We handle them by checking the word after BEGIN/END

        private NavigateMatchingPairCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdNavigateMatchingPair);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static NavigateMatchingPairCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new NavigateMatchingPairCommand(package, commandService);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (sender is OleMenuCommand cmd)
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) { cmd.Enabled = false; return; }
                textManager.GetActiveView(1, null, out var textView);
                cmd.Enabled = textView != null;
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) return;

                textManager.GetActiveView(1, null, out var textView);
                if (textView == null) return;

                textView.GetBuffer(out var buffer);
                if (buffer == null) return;

                textView.GetCaretPos(out var caretLine, out var caretCol);
                buffer.GetPositionOfLineIndex(caretLine, caretCol, out var cursorOffset);

                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var documentText);

                if (string.IsNullOrEmpty(documentText)) return;

                // Get word at caret
                var word = GetWordAtOffset(documentText, cursorOffset);
                if (string.IsNullOrEmpty(word)) return;

                int matchOffset = -1;

                // Check for parentheses (single character)
                if (word == "(" || word == ")")
                {
                    matchOffset = FindMatchingParen(documentText, cursorOffset, word == "(");
                }
                else if (OpenToClose.ContainsKey(word))
                {
                    // Forward search
                    matchOffset = FindMatchingKeywordForward(documentText, cursorOffset, word, OpenToClose[word]);
                }
                else if (CloseToOpen.ContainsKey(word))
                {
                    // Backward search
                    matchOffset = FindMatchingKeywordBackward(documentText, cursorOffset, word, CloseToOpen[word]);
                }
                else if (string.Equals(word, "TRY", StringComparison.OrdinalIgnoreCase))
                {
                    // Could be BEGIN TRY or END TRY — check context
                    matchOffset = HandleTryCatch(documentText, cursorOffset, "TRY");
                }
                else if (string.Equals(word, "CATCH", StringComparison.OrdinalIgnoreCase))
                {
                    matchOffset = HandleTryCatch(documentText, cursorOffset, "CATCH");
                }

                if (matchOffset >= 0 && matchOffset <= documentText.Length)
                {
                    buffer.GetLineIndexOfPosition(matchOffset, out var targetLine, out var targetCol);
                    textView.SetCaretPos(targetLine, targetCol);
                    textView.CenterLines(targetLine, 1);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "NavigateMatchingPair command failed");
            }
        }

        /// <summary>
        /// Extracts the word at the given offset. For parentheses, returns the single character.
        /// </summary>
        private static string GetWordAtOffset(string text, int offset)
        {
            if (offset < 0 || offset >= text.Length)
                return string.Empty;

            var ch = text[offset];

            // Parentheses
            if (ch == '(' || ch == ')')
                return ch.ToString();

            // Check character before if offset is at whitespace
            if (!char.IsLetterOrDigit(ch) && ch != '_')
            {
                if (offset > 0)
                {
                    ch = text[offset - 1];
                    offset--;
                    if (ch == '(' || ch == ')')
                        return ch.ToString();
                    if (!char.IsLetterOrDigit(ch) && ch != '_')
                        return string.Empty;
                }
                else return string.Empty;
            }

            // Walk back to start of word
            int start = offset;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
                start--;

            // Walk forward to end of word
            int end = offset;
            while (end < text.Length - 1 && (char.IsLetterOrDigit(text[end + 1]) || text[end + 1] == '_'))
                end++;

            return text.Substring(start, end - start + 1);
        }

        /// <summary>
        /// Stack-based parenthesis matcher.
        /// </summary>
        private static int FindMatchingParen(string text, int offset, bool forward)
        {
            char open = '(', close = ')';
            int depth = 0;

            if (forward)
            {
                for (int i = offset; i < text.Length; i++)
                {
                    if (text[i] == open) depth++;
                    else if (text[i] == close)
                    {
                        depth--;
                        if (depth == 0) return i;
                    }
                }
            }
            else
            {
                for (int i = offset; i >= 0; i--)
                {
                    if (text[i] == close) depth++;
                    else if (text[i] == open)
                    {
                        depth--;
                        if (depth == 0) return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Stack-based forward search for matching keyword (e.g., BEGIN -> END).
        /// Skips nested BEGIN/END blocks.
        /// </summary>
        private static int FindMatchingKeywordForward(string text, int offset, string openKeyword, string closeKeyword)
        {
            int depth = 0;
            int i = offset;
            int textLen = text.Length;

            while (i < textLen)
            {
                SkipNonWord(text, ref i, textLen);
                if (i >= textLen) break;

                var word = ReadWord(text, ref i);
                if (string.IsNullOrEmpty(word)) { i++; continue; }

                if (string.Equals(word, openKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    depth++;
                }
                else if (string.Equals(word, closeKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    depth--;
                    if (depth == 0)
                        return i - word.Length;
                }
            }

            return -1;
        }

        /// <summary>
        /// Stack-based backward search for matching keyword (e.g., END -> BEGIN).
        /// </summary>
        private static int FindMatchingKeywordBackward(string text, int offset, string closeKeyword, string openKeyword)
        {
            int depth = 0;

            // Collect all keywords and their positions, then walk backward
            var keywords = new List<(int offset, string word)>();
            int i = 0;
            while (i < text.Length)
            {
                SkipNonWord(text, ref i, text.Length);
                if (i >= text.Length) break;

                int wordStart = i;
                var word = ReadWord(text, ref i);
                if (string.IsNullOrEmpty(word)) { i++; continue; }

                if (string.Equals(word, openKeyword, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(word, closeKeyword, StringComparison.OrdinalIgnoreCase) ||
                    // Also track CASE for CASE/END pairs
                    string.Equals(word, "CASE", StringComparison.OrdinalIgnoreCase))
                {
                    keywords.Add((wordStart, word));
                }
            }

            // Walk backward from the current position
            for (int k = keywords.Count - 1; k >= 0; k--)
            {
                if (keywords[k].offset > offset) continue;

                if (string.Equals(keywords[k].word, closeKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    depth++;
                }
                else if (string.Equals(keywords[k].word, openKeyword, StringComparison.OrdinalIgnoreCase) ||
                         // END can match CASE as well
                         (string.Equals(closeKeyword, "END", StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(keywords[k].word, "CASE", StringComparison.OrdinalIgnoreCase)))
                {
                    depth--;
                    if (depth == 0)
                        return keywords[k].offset;
                }
            }

            return -1;
        }

        /// <summary>
        /// Handle TRY/CATCH navigation: find the matching BEGIN TRY / END TRY or BEGIN CATCH / END CATCH.
        /// </summary>
        private static int HandleTryCatch(string text, int offset, string keyword)
        {
            // Look backwards from offset to see if preceded by BEGIN or END
            var precedingWord = GetPrecedingKeyword(text, offset);

            if (string.Equals(precedingWord, "BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                // BEGIN TRY/CATCH -> find END TRY/CATCH forward
                return FindCompoundKeywordForward(text, offset, "END", keyword);
            }
            else if (string.Equals(precedingWord, "END", StringComparison.OrdinalIgnoreCase))
            {
                // END TRY/CATCH -> find BEGIN TRY/CATCH backward
                return FindCompoundKeywordBackward(text, offset, "BEGIN", keyword);
            }

            return -1;
        }

        private static string GetPrecedingKeyword(string text, int offset)
        {
            // Walk back past whitespace
            int i = offset - 1;
            while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
            if (i < 0) return string.Empty;

            // Walk back through word characters
            int end = i;
            while (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_')) i--;

            return text.Substring(i, end - i + 1);
        }

        private static int FindCompoundKeywordForward(string text, int offset, string target, string qualifier)
        {
            int i = offset;
            while (i < text.Length)
            {
                SkipNonWord(text, ref i, text.Length);
                if (i >= text.Length) break;

                int wordStart = i;
                var word = ReadWord(text, ref i);
                if (string.Equals(word, target, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if next word is the qualifier
                    int j = i;
                    SkipNonWord(text, ref j, text.Length);
                    if (j < text.Length)
                    {
                        var nextWord = ReadWord(text, ref j);
                        if (string.Equals(nextWord, qualifier, StringComparison.OrdinalIgnoreCase))
                            return wordStart;
                    }
                }
            }
            return -1;
        }

        private static int FindCompoundKeywordBackward(string text, int offset, string target, string qualifier)
        {
            // Collect all target+qualifier pairs
            var pairs = new List<int>();
            int i = 0;
            while (i < text.Length)
            {
                SkipNonWord(text, ref i, text.Length);
                if (i >= text.Length) break;

                int wordStart = i;
                var word = ReadWord(text, ref i);
                if (string.Equals(word, target, StringComparison.OrdinalIgnoreCase))
                {
                    int j = i;
                    SkipNonWord(text, ref j, text.Length);
                    if (j < text.Length)
                    {
                        var nextWord = ReadWord(text, ref j);
                        if (string.Equals(nextWord, qualifier, StringComparison.OrdinalIgnoreCase))
                            pairs.Add(wordStart);
                    }
                }
            }

            // Find the nearest one before offset
            for (int k = pairs.Count - 1; k >= 0; k--)
            {
                if (pairs[k] < offset)
                    return pairs[k];
            }
            return -1;
        }

        /// <summary>
        /// Advances past whitespace and non-word characters.
        /// </summary>
        private static void SkipNonWord(string text, ref int i, int length)
        {
            while (i < length && !char.IsLetterOrDigit(text[i]) && text[i] != '_')
                i++;
        }

        /// <summary>
        /// Reads a word (sequence of letters/digits/underscore) starting at position i.
        /// Advances i past the word.
        /// </summary>
        private static string ReadWord(string text, ref int i)
        {
            int start = i;
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                i++;
            return i > start ? text.Substring(start, i - start) : string.Empty;
        }
    }
}
