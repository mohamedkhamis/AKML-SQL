#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// MEF ITagger that highlights matching bracket pairs in T-SQL: parentheses,
    /// BEGIN/END, CASE/END, TRY/CATCH/END TRY, IF/ELSE.
    /// Uses a stack-based scanner to handle nesting correctly.
    /// </summary>
    internal sealed class BracketMatchingTagger : ITagger<ITextMarkerTag>, IDisposable
    {
        private readonly ITextView _textView;
        private readonly ITextBuffer _buffer;
        private Timer? _debounceTimer;
        private SnapshotSpan? _matchSpanA;
        private SnapshotSpan? _matchSpanB;
        private ITextSnapshot? _currentSnapshot;
        private readonly object _updateLock = new object();
        private bool _disposed;

        private const int DebounceMs = 100;

        /// <summary>
        /// T-SQL block keyword pairs. The first keyword opens a block and the second closes it.
        /// Each pair also specifies whether the close keyword is shared (like END).
        /// </summary>
        private static readonly (string Open, string Close)[] BlockPairs = new[]
        {
            ("BEGIN", "END"),
            ("CASE", "END"),
            ("BEGIN TRY", "END TRY"),
            ("BEGIN CATCH", "END CATCH"),
        };

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        internal BracketMatchingTagger(ITextView textView, ITextBuffer buffer)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

            _textView.Caret.PositionChanged += OnCaretPositionChanged;
            _textView.LayoutChanged += OnLayoutChanged;
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (e.NewSnapshot != e.OldSnapshot)
                ScheduleUpdate();
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            ScheduleUpdate();
        }

        private void ScheduleUpdate()
        {
            if (_disposed) return;
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(OnDebounceElapsed, null, DebounceMs, Timeout.Infinite);
        }

        private void OnDebounceElapsed(object? state)
        {
            if (_disposed) return;
            try
            {
                UpdateBracketMatch();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "BracketMatchingTagger: error during update");
            }
        }

        private void UpdateBracketMatch()
        {
            var caretPosition = _textView.Caret.Position.BufferPosition;
            var snapshot = caretPosition.Snapshot;
            SnapshotSpan? spanA = null;
            SnapshotSpan? spanB = null;

            // Check for parenthesis matching first
            var parenMatch = FindMatchingParen(caretPosition, snapshot);
            if (parenMatch.HasValue)
            {
                spanA = new SnapshotSpan(snapshot, caretPosition.Position, 1);
                spanB = new SnapshotSpan(snapshot, parenMatch.Value, 1);
            }
            else
            {
                // Check for keyword block matching (BEGIN/END, CASE/END, etc.)
                var keywordMatch = FindMatchingKeyword(caretPosition, snapshot);
                if (keywordMatch.HasValue)
                {
                    spanA = keywordMatch.Value.Item1;
                    spanB = keywordMatch.Value.Item2;
                }
            }

            lock (_updateLock)
            {
                _matchSpanA = spanA;
                _matchSpanB = spanB;
                _currentSnapshot = snapshot;
            }

            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        /// <summary>
        /// Finds the matching parenthesis for the one at the caret position.
        /// Uses a stack-based scan to handle nesting.
        /// </summary>
        private static int? FindMatchingParen(SnapshotPoint position, ITextSnapshot snapshot)
        {
            if (position.Position >= snapshot.Length)
                return null;

            var ch = snapshot[position.Position];

            // Also check the character before the caret
            char chBefore = position.Position > 0 ? snapshot[position.Position - 1] : '\0';

            int searchStart;
            char openChar, closeChar;
            int direction;

            if (ch == '(')
            {
                searchStart = position.Position + 1;
                openChar = '(';
                closeChar = ')';
                direction = 1;
            }
            else if (ch == ')')
            {
                searchStart = position.Position - 1;
                openChar = ')';
                closeChar = '(';
                direction = -1;
            }
            else if (chBefore == '(')
            {
                // Caret is right after '('
                return null;
            }
            else if (chBefore == ')')
            {
                searchStart = position.Position - 2;
                openChar = ')';
                closeChar = '(';
                direction = -1;
            }
            else
            {
                return null;
            }

            int depth = 1;
            for (int i = searchStart; i >= 0 && i < snapshot.Length; i += direction)
            {
                var c = snapshot[i];
                if (c == openChar)
                    depth++;
                else if (c == closeChar)
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds matching T-SQL block keywords (BEGIN/END, CASE/END, etc.) around the caret.
        /// </summary>
        private static (SnapshotSpan, SnapshotSpan)? FindMatchingKeyword(
            SnapshotPoint position, ITextSnapshot snapshot)
        {
            var text = snapshot.GetText();
            var wordAtCaret = GetWordAtPosition(text, position.Position);

            if (string.IsNullOrEmpty(wordAtCaret.Word))
                return null;

            var word = wordAtCaret.Word.ToUpperInvariant();
            var wordStart = wordAtCaret.Start;

            // Check for compound keywords: BEGIN TRY, BEGIN CATCH, END TRY, END CATCH
            var compoundWord = TryGetCompoundKeyword(text, wordStart, word);
            if (compoundWord != null)
            {
                word = compoundWord.Value.CompoundWord;
                wordStart = compoundWord.Value.Start;
            }

            // Find the matching pair
            foreach (var pair in BlockPairs)
            {
                if (word == pair.Open)
                {
                    var matchPos = FindForwardMatch(text, wordStart + word.Length, pair.Open, pair.Close);
                    if (matchPos >= 0)
                    {
                        return (
                            new SnapshotSpan(snapshot, wordStart, word.Length),
                            new SnapshotSpan(snapshot, matchPos, pair.Close.Length));
                    }
                }
                else if (word == pair.Close)
                {
                    var matchPos = FindBackwardMatch(text, wordStart, pair.Open, pair.Close);
                    if (matchPos >= 0)
                    {
                        return (
                            new SnapshotSpan(snapshot, wordStart, word.Length),
                            new SnapshotSpan(snapshot, matchPos, pair.Open.Length));
                    }
                }
            }

            // Special handling for standalone BEGIN/END (not part of TRY/CATCH)
            if (word == "BEGIN")
            {
                var matchPos = FindForwardMatch(text, wordStart + 5, "BEGIN", "END");
                if (matchPos >= 0)
                {
                    return (
                        new SnapshotSpan(snapshot, wordStart, 5),
                        new SnapshotSpan(snapshot, matchPos, 3));
                }
            }
            else if (word == "END")
            {
                var matchPos = FindBackwardMatch(text, wordStart, "BEGIN", "END");
                if (matchPos >= 0)
                {
                    return (
                        new SnapshotSpan(snapshot, wordStart, 3),
                        new SnapshotSpan(snapshot, matchPos, 5));
                }
            }

            return null;
        }

        /// <summary>
        /// Scans forward from <paramref name="startPos"/> looking for the matching close keyword,
        /// using a depth counter to handle nesting.
        /// </summary>
        private static int FindForwardMatch(string text, int startPos, string openKeyword, string closeKeyword)
        {
            int depth = 1;
            int i = startPos;

            while (i < text.Length)
            {
                // Skip whitespace
                if (char.IsWhiteSpace(text[i]))
                {
                    i++;
                    continue;
                }

                // Skip non-identifier characters
                if (!IsIdentifierStartChar(text[i]))
                {
                    i++;
                    continue;
                }

                // Extract word at this position
                var (word, _, end) = ExtractWord(text, i);
                var upper = word.ToUpperInvariant();

                // Check for compound keywords
                var compound = TryGetCompoundKeyword(text, i, upper);
                if (compound != null)
                {
                    upper = compound.Value.CompoundWord;
                    end = compound.Value.End;
                }

                if (upper == openKeyword)
                    depth++;
                else if (upper == closeKeyword)
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }

                i = end;
            }

            return -1;
        }

        /// <summary>
        /// Scans backward from <paramref name="startPos"/> looking for the matching open keyword.
        /// </summary>
        private static int FindBackwardMatch(string text, int startPos, string openKeyword, string closeKeyword)
        {
            int depth = 1;
            int i = startPos - 1;

            while (i >= 0)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    i--;
                    continue;
                }

                if (!IsIdentifierChar(text[i]))
                {
                    i--;
                    continue;
                }

                // Walk backward to find word start
                int wordEnd = i + 1;
                while (i >= 0 && IsIdentifierChar(text[i]))
                    i--;

                int wordStart = i + 1;
                var word = text.Substring(wordStart, wordEnd - wordStart).ToUpperInvariant();

                // Check for compound keywords (need to look at what follows this word)
                var compound = TryGetCompoundKeyword(text, wordStart, word);
                if (compound != null)
                {
                    word = compound.Value.CompoundWord;
                }

                if (word == closeKeyword)
                    depth++;
                else if (word == openKeyword)
                {
                    depth--;
                    if (depth == 0)
                        return wordStart;
                }

                // Continue backward from before the word
                i = wordStart - 1;
            }

            return -1;
        }

        private static (string Word, int Start, int End) ExtractWord(string text, int pos)
        {
            int start = pos;
            int end = pos;
            while (end < text.Length && IsIdentifierChar(text[end]))
                end++;
            return (text.Substring(start, end - start), start, end);
        }

        private static (string CompoundWord, int Start, int End)? TryGetCompoundKeyword(
            string text, int wordStart, string word)
        {
            if (word == "BEGIN" || word == "END")
            {
                // Look ahead past whitespace for TRY or CATCH
                var nextWord = GetNextWord(text, wordStart + word.Length);
                if (nextWord != null)
                {
                    var nextUpper = nextWord.Value.Word.ToUpperInvariant();
                    if (nextUpper == "TRY" || nextUpper == "CATCH")
                    {
                        return (word + " " + nextUpper, wordStart, nextWord.Value.End);
                    }
                }
            }

            return null;
        }

        private static (string Word, int Start, int End)? GetNextWord(string text, int pos)
        {
            // Skip whitespace
            while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                pos++;

            if (pos >= text.Length || !IsIdentifierStartChar(text[pos]))
                return null;

            int start = pos;
            while (pos < text.Length && IsIdentifierChar(text[pos]))
                pos++;

            return (text.Substring(start, pos - start), start, pos);
        }

        private static (string Word, int Start) GetWordAtPosition(string text, int position)
        {
            if (position >= text.Length)
                return (string.Empty, 0);

            int start = position;
            while (start > 0 && IsIdentifierChar(text[start - 1]))
                start--;

            int end = position;
            while (end < text.Length && IsIdentifierChar(text[end]))
                end++;

            if (start == end)
                return (string.Empty, 0);

            return (text.Substring(start, end - start), start);
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@';
        }

        private static bool IsIdentifierStartChar(char c)
        {
            return char.IsLetter(c) || c == '_' || c == '#' || c == '@';
        }

        public IEnumerable<ITagSpan<ITextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            SnapshotSpan? spanA, spanB;
            lock (_updateLock)
            {
                spanA = _matchSpanA;
                spanB = _matchSpanB;
            }

            if (spanA == null || spanB == null || spans.Count == 0)
                yield break;

            var currentSnapshot = spans[0].Snapshot;
            var tag = new TextMarkerTag("bracehighlight");

            var translatedA = spanA.Value.Snapshot == currentSnapshot
                ? spanA.Value
                : spanA.Value.TranslateTo(currentSnapshot, SpanTrackingMode.EdgeExclusive);

            var translatedB = spanB.Value.Snapshot == currentSnapshot
                ? spanB.Value
                : spanB.Value.TranslateTo(currentSnapshot, SpanTrackingMode.EdgeExclusive);

            foreach (var requestedSpan in spans)
            {
                if (translatedA.OverlapsWith(requestedSpan))
                {
                    yield return new TagSpan<ITextMarkerTag>(translatedA, tag);
                }

                if (translatedB.OverlapsWith(requestedSpan))
                {
                    yield return new TagSpan<ITextMarkerTag>(translatedB, tag);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _textView.Caret.PositionChanged -= OnCaretPositionChanged;
            _textView.LayoutChanged -= OnLayoutChanged;
            _debounceTimer?.Dispose();
        }
    }
}
