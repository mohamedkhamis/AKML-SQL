#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Per-line analysis indicator in the editor glyph margin (SQL Prompt parity): one
    /// severity-coloured dot per line with issues, tooltip listing them, left-click opening the
    /// fix / suppress / disable menu (<see cref="WarningGlyphMenu"/>). Modeled 1:1 on the
    /// proven <c>Navigation\BookmarkGlyphFactory.cs</c> tag + tagger + glyph-factory trio.
    /// </summary>
    internal sealed class WarningGlyphTag : IGlyphTag
    {
        public WarningGlyphTag(IReadOnlyList<CodeIssueInfo> issuesOnLine, int maxSeverity)
        {
            IssuesOnLine = issuesOnLine;
            MaxSeverity  = maxSeverity;
        }

        public IReadOnlyList<CodeIssueInfo> IssuesOnLine { get; }
        public int MaxSeverity { get; }
    }

    /// <summary>
    /// Pure per-line aggregation, extracted for unit testing: 1-based issue lines → 0-based
    /// snapshot lines, one entry per line, out-of-snapshot lines dropped (stale-offset policy
    /// matching <see cref="DiagnosticTagger"/>).
    /// </summary>
    internal static class WarningGlyphLineIndex
    {
        public static Dictionary<int, List<CodeIssueInfo>> GroupByLine(
            IReadOnlyList<CodeIssueInfo> issues, int snapshotLineCount)
        {
            var byLine = new Dictionary<int, List<CodeIssueInfo>>();
            foreach (var issue in issues)
            {
                var line = issue.Line - 1;   // engine lines are 1-based
                if (line < 0 || line >= snapshotLineCount) continue;
                if (!byLine.TryGetValue(line, out var list))
                {
                    list = new List<CodeIssueInfo>();
                    byLine[line] = list;
                }
                list.Add(issue);
            }
            return byLine;
        }

        public static int MaxSeverity(IReadOnlyList<CodeIssueInfo> issues)
        {
            var max = 0;
            foreach (var issue in issues)
                if (issue.Severity > max) max = issue.Severity;
            return max;
        }
    }

    internal sealed class WarningGlyphTagger : ITagger<WarningGlyphTag>
    {
        private readonly ITextBuffer _buffer;
        private AnalysisController? _controller;

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        public WarningGlyphTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            TryHookController();
        }

        /// <summary>
        /// The AnalysisController is created by <see cref="DiagnosticTaggerProvider"/> (a VIEW
        /// tagger); this BUFFER tagger may be constructed first. Resolve lazily and never
        /// construct a second controller.
        /// </summary>
        private bool TryHookController()
        {
            if (_controller != null) return true;
            if (!_buffer.Properties.TryGetProperty(typeof(AnalysisController), out AnalysisController controller))
                return false;

            _controller = controller;
            controller.DiagnosticsUpdated += (_, e) =>
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                    new SnapshotSpan(e.Snapshot, 0, e.Snapshot.Length)));
            return true;
        }

        public IEnumerable<ITagSpan<WarningGlyphTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (!TryHookController() || spans.Count == 0) yield break;

            var snapshot = spans[0].Snapshot;
            var byLine = WarningGlyphLineIndex.GroupByLine(_controller!.CurrentIssues, snapshot.LineCount);
            if (byLine.Count == 0) yield break;

            foreach (var span in spans)
            {
                var startLine = snapshot.GetLineNumberFromPosition(span.Start.Position);
                var endLine   = snapshot.GetLineNumberFromPosition(span.End.Position);
                for (var i = startLine; i <= endLine; i++)
                {
                    if (!byLine.TryGetValue(i, out var issues)) continue;
                    var line = snapshot.GetLineFromLineNumber(i);
                    yield return new TagSpan<WarningGlyphTag>(
                        new SnapshotSpan(line.Start, line.End),
                        new WarningGlyphTag(issues, WarningGlyphLineIndex.MaxSeverity(issues)));
                }
            }
        }
    }

    [Export(typeof(ITaggerProvider))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [TagType(typeof(WarningGlyphTag))]
    internal sealed class WarningGlyphTaggerProvider : ITaggerProvider
    {
        public ITagger<T>? CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            if (buffer == null) return null;
            return buffer.Properties.GetOrCreateSingletonProperty(
                typeof(WarningGlyphTagger),
                () => (ITagger<T>)(object)new WarningGlyphTagger(buffer));
        }
    }

    [Export(typeof(IGlyphFactoryProvider))]
    [Name("AkmlSqlWarningGlyph")]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [TagType(typeof(WarningGlyphTag))]
    [Order(After = "VsTextMarker")]
    internal sealed class WarningGlyphFactoryProvider : IGlyphFactoryProvider
    {
        public IGlyphFactory GetGlyphFactory(IWpfTextView view, IWpfTextViewMargin margin)
        {
            // Same buffer-properties handshake as the bookmark glyph (split-view safe).
            view.TextBuffer.Properties.GetOrCreateSingletonProperty("AkmlSqlTextView", () => (ITextView)view);
            return new WarningGlyphFactory(view.TextBuffer);
        }
    }

    internal sealed class WarningGlyphFactory : IGlyphFactory
    {
        // Semantic severity colours (permitted hardcoded hex): they must read the same in every
        // theme. Frozen statics per the project WPF conventions.
        private static readonly SolidColorBrush ErrorBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00)));
        private static readonly SolidColorBrush WarningBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00)));
        private static readonly SolidColorBrush InfoBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF)));

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        private readonly ITextBuffer _buffer;

        public WarningGlyphFactory(ITextBuffer buffer) => _buffer = buffer;

        public UIElement? GenerateGlyph(IWpfTextViewLine line, IGlyphTag tag)
        {
            if (tag is not WarningGlyphTag warningTag) return null;

            var glyph = new Ellipse
            {
                Width  = 10,
                Height = 10,
                Fill   = warningTag.MaxSeverity switch
                {
                    >= 3 => ErrorBrush,
                    2    => WarningBrush,
                    _    => InfoBrush,
                },
                Margin  = new Thickness(3),
                Cursor  = Cursors.Hand,
                ToolTip = BuildTooltip(warningTag.IssuesOnLine),
            };

            glyph.MouseLeftButtonDown += (sender, e) =>
            {
                e.Handled = true;
                var menu = WarningGlyphMenu.Build(_buffer, warningTag.IssuesOnLine);
                menu.PlacementTarget = (UIElement)sender;
                menu.IsOpen = true;
            };

            return glyph;
        }

        private static string BuildTooltip(IReadOnlyList<CodeIssueInfo> issues)
        {
            var lines = new List<string>(issues.Count + 1);
            foreach (var issue in issues)
                lines.Add($"{issue.RuleId}: {issue.Message}");
            lines.Add("Click for fixes");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
