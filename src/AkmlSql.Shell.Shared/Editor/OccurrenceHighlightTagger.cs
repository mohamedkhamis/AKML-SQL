#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// MEF ITagger that highlights all occurrences of the word under the caret in the T-SQL editor.
    /// Uses a 150ms debounce timer to avoid excessive re-scanning on rapid caret movement.
    /// Performs case-insensitive whole-word matching across the entire buffer.
    /// </summary>
    internal sealed class OccurrenceHighlightTagger : ITagger<ITextMarkerTag>, IDisposable
    {
        private readonly ITextView _textView;
        private readonly ITextBuffer _buffer;
        private Timer? _debounceTimer;
        private NormalizedSnapshotSpanCollection _wordSpans = new NormalizedSnapshotSpanCollection();
        private string _currentWord = string.Empty;
        private ITextSnapshot? _currentSnapshot;
        private readonly object _updateLock = new object();
        private bool _disposed;

        /// <summary>Debounce interval for caret position changes.</summary>
        private const int DebounceMs = 150;

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        internal OccurrenceHighlightTagger(ITextView textView, ITextBuffer buffer)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

            _textView.Caret.PositionChanged += OnCaretPositionChanged;
            _textView.LayoutChanged += OnLayoutChanged;
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            // If a new snapshot was generated, re-evaluate
            if (e.NewSnapshot != e.OldSnapshot)
            {
                ScheduleUpdate();
            }
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
                UpdateWordAdornments();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "OccurrenceHighlightTagger: error during update");
            }
        }

        private void UpdateWordAdornments()
        {
            var caretPosition = _textView.Caret.Position.BufferPosition;
            var snapshot = caretPosition.Snapshot;

            // Extract the word at the caret position
            var word = GetWordAtPosition(caretPosition);
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
            {
                // Clear highlights if caret is not on a meaningful word
                ClearHighlights(snapshot);
                return;
            }

            // If the word hasn't changed, no need to re-scan
            if (word.Equals(_currentWord, StringComparison.OrdinalIgnoreCase) && _currentSnapshot == snapshot)
            {
                return;
            }

            // Find all whole-word case-insensitive matches in the buffer
            var wordSpans = new List<SnapshotSpan>();
            var text = snapshot.GetText();
            var pattern = @"\b" + Regex.Escape(word) + @"\b";

            try
            {
                var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    wordSpans.Add(new SnapshotSpan(snapshot, match.Index, match.Length));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return;
            }

            // Only highlight if there are at least 2 occurrences (the word itself + at least one other)
            if (wordSpans.Count < 2)
            {
                ClearHighlights(snapshot);
                return;
            }

            lock (_updateLock)
            {
                _currentWord = word;
                _currentSnapshot = snapshot;
                _wordSpans = new NormalizedSnapshotSpanCollection(wordSpans);
            }

            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        private void ClearHighlights(ITextSnapshot snapshot)
        {
            lock (_updateLock)
            {
                if (_wordSpans.Count == 0) return;
                _currentWord = string.Empty;
                _currentSnapshot = null;
                _wordSpans = new NormalizedSnapshotSpanCollection();
            }

            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        /// <summary>
        /// Extracts the word (SQL identifier) at the given buffer position.
        /// </summary>
        private static string GetWordAtPosition(SnapshotPoint position)
        {
            var line = position.GetContainingLine();
            var lineText = line.GetText();
            var col = position.Position - line.Start.Position;

            if (col >= lineText.Length)
                return string.Empty;

            // Walk backwards to find word start
            int start = col;
            while (start > 0 && IsIdentifierChar(lineText[start - 1]))
                start--;

            // Walk forwards to find word end
            int end = col;
            while (end < lineText.Length && IsIdentifierChar(lineText[end]))
                end++;

            if (start == end)
                return string.Empty;

            return lineText.Substring(start, end - start);
        }

        /// <summary>
        /// Returns true if the character can be part of a SQL identifier.
        /// </summary>
        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@';
        }

        public IEnumerable<ITagSpan<ITextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            NormalizedSnapshotSpanCollection wordSpans;
            lock (_updateLock)
            {
                wordSpans = _wordSpans;
            }

            if (wordSpans.Count == 0 || spans.Count == 0)
                yield break;

            var currentSnapshot = spans[0].Snapshot;

            foreach (var span in wordSpans)
            {
                // Translate span to the current snapshot if needed
                var translatedSpan = span.Snapshot == currentSnapshot
                    ? span
                    : span.TranslateTo(currentSnapshot, SpanTrackingMode.EdgeExclusive);

                // Only emit tags for spans that overlap the requested region
                foreach (var requestedSpan in spans)
                {
                    if (translatedSpan.OverlapsWith(requestedSpan))
                    {
                        yield return new TagSpan<ITextMarkerTag>(
                            translatedSpan,
                            new TextMarkerTag("MarkerFormatDefinition/HighlightWordFormatDefinition"));
                        break;
                    }
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
