#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace AkmlSql.Shell.Shared.Navigation
{
    /// <summary>
    /// Tag type for bookmark glyph indicators in the editor margin.
    /// </summary>
    internal class BookmarkTag : IGlyphTag { }

    /// <summary>
    /// Tagger that checks <see cref="BookmarkManager"/> for each visible line
    /// and produces <see cref="BookmarkTag"/> spans for bookmarked lines.
    /// </summary>
    internal sealed class BookmarkTagger : ITagger<BookmarkTag>
    {
        private readonly ITextView? _textView;
        private readonly string _textViewId;

        public BookmarkTagger(ITextView? textView)
        {
            _textView = textView;
            // Use textView's buffer if available, otherwise this tagger won't have a valid ID
            // until a view is associated later via the glyph factory provider
            var buffer = textView?.TextBuffer;
            _textViewId = buffer?.Properties.GetOrCreateSingletonProperty(
                "AkmlSqlTextViewId", () => Guid.NewGuid().ToString("N")) ?? string.Empty;
        }

        /// <summary>
        /// Gets the text view ID used by this tagger for bookmark lookups.
        /// </summary>
        public string TextViewId => _textViewId;

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        public IEnumerable<ITagSpan<BookmarkTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            foreach (var span in spans)
            {
                var snapshot = span.Snapshot;
                var startLine = snapshot.GetLineNumberFromPosition(span.Start.Position);
                var endLine = snapshot.GetLineNumberFromPosition(span.End.Position);
                for (int i = startLine; i <= endLine; i++)
                {
                    if (BookmarkManager.IsBookmarked(_textViewId, i))
                    {
                        var line = snapshot.GetLineFromLineNumber(i);
                        yield return new TagSpan<BookmarkTag>(
                            new SnapshotSpan(line.Start, line.End),
                            new BookmarkTag());
                    }
                }
            }
        }

        /// <summary>
        /// Forces a re-evaluation of all tags (call after bookmark toggle).
        /// </summary>
        public void Refresh()
        {
            if (_textView == null) return;
            var snapshot = _textView.TextSnapshot;
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }
    }

    [Export(typeof(ITaggerProvider))]
    [ContentType("T-SQL")]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TagType(typeof(BookmarkTag))]
    internal sealed class BookmarkTaggerProvider : ITaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            // Store the tagger in buffer properties for later retrieval by BookmarkCommands.
            // The tagger is created lazily on first request and reused for the buffer lifetime.
            return buffer.Properties.GetOrCreateSingletonProperty(
                typeof(BookmarkTagger),
                () => (ITagger<T>)(object)new BookmarkTagger(
                    GetTextViewForBuffer(buffer) ?? GetOrCreateDummyView(buffer)));
        }

        /// <summary>
        /// Attempts to find the ITextView associated with a buffer from stored properties.
        /// Returns null if no view is associated yet.
        /// </summary>
        private static ITextView? GetTextViewForBuffer(ITextBuffer buffer)
        {
            if (buffer.Properties.TryGetProperty("AkmlSqlTextView", out ITextView view))
                return view;
            return null;
        }

        /// <summary>
        /// Returns null when no view is available yet. The tagger's Refresh()
        /// guards against null _textView, and GetTags works off the buffer
        /// properties alone.
        /// </summary>
        private static ITextView? GetOrCreateDummyView(ITextBuffer buffer)
        {
            // Return null — tagger handles this gracefully via null guard in Refresh()
            return null;
        }
    }

    [Export(typeof(IGlyphFactoryProvider))]
    [Name("AkmlSqlBookmarkGlyph")]
    [ContentType("T-SQL")]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TagType(typeof(BookmarkTag))]
    [Order(After = "VsTextMarker")]
    internal sealed class BookmarkGlyphFactoryProvider : IGlyphFactoryProvider
    {
        public IGlyphFactory GetGlyphFactory(IWpfTextView view, IWpfTextViewMargin margin)
        {
            // Store the view reference in the buffer for the tagger to find.
            // Use GetOrCreateSingletonProperty to handle split views safely (AddProperty throws on duplicates).
            view.TextBuffer.Properties.GetOrCreateSingletonProperty("AkmlSqlTextView", () => (ITextView)view);
            return new BookmarkGlyphFactory();
        }
    }

    /// <summary>
    /// Generates a blue circle glyph for bookmark indicators in the editor margin.
    /// </summary>
    internal sealed class BookmarkGlyphFactory : IGlyphFactory
    {
        public UIElement? GenerateGlyph(IWpfTextViewLine line, IGlyphTag tag)
        {
            if (tag is not BookmarkTag) return null;

            return new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF)), // Blue
                Margin = new Thickness(2, 2, 2, 2)
            };
        }
    }
}
