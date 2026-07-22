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
            return new WarningGlyphFactory();
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

        public UIElement? GenerateGlyph(IWpfTextViewLine line, IGlyphTag tag)
        {
            if (tag is not WarningGlyphTag warningTag) return null;

            // NO mouse handlers here: the glyph margin routes input through its own mouse
            // processor chain, so element-level handlers on glyph visuals never fire (verified
            // in SSMS 22 — clicks were silently dead). Clicks are handled by
            // WarningGlyphMouseProcessor via IGlyphMouseProcessorProvider, the same mechanism
            // breakpoint glyphs use.
            return new Ellipse
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

    /// <summary>
    /// Click handling for the warning glyphs. The glyph margin does not deliver mouse events to
    /// glyph visuals — <see cref="IGlyphMouseProcessorProvider"/> is the sanctioned hook (it is
    /// how breakpoint clicks work): map the click's Y coordinate to a view line, look up that
    /// line's issues, open the fix menu.
    /// </summary>
    [Export(typeof(IGlyphMouseProcessorProvider))]
    [Name("AkmlSqlWarningGlyphMouseProcessor")]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    internal sealed class WarningGlyphMouseProcessorProvider : IGlyphMouseProcessorProvider
    {
        public IMouseProcessor GetAssociatedMouseProcessor(
            IWpfTextViewHost wpfTextViewHost, IWpfTextViewMargin margin)
            => new WarningGlyphMouseProcessor(wpfTextViewHost, margin);
    }

    internal sealed class WarningGlyphMouseProcessor : MouseProcessorBase
    {
        private readonly IWpfTextViewHost _host;
        private readonly IWpfTextViewMargin _margin;
        private IReadOnlyList<CodeIssueInfo>? _pendingIssues;
        private int _pendingLine;

        public WarningGlyphMouseProcessor(IWpfTextViewHost host, IWpfTextViewMargin margin)
        {
            _host   = host;
            _margin = margin;
        }

        /// <summary>
        /// Arm on mouse-DOWN (and swallow it so the margin does nothing else) but open the menu
        /// on mouse-UP, deferred one dispatcher beat: a ContextMenu opened during the down event
        /// is dismissed immediately by the margin's ensuing capture/button-up sequence — the
        /// logs showed "opening fix menu" on every click while the user saw nothing.
        /// </summary>
        public override void PreprocessMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            try
            {
                _pendingIssues = null;
                if (!TryGetIssuesAt(e, out _pendingLine, out var issues)) return;
                _pendingIssues = issues;
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "WarningGlyph: glyph-margin mouse-down handling failed");
            }
        }

        public override void PreprocessMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            try
            {
                var issues = _pendingIssues;
                _pendingIssues = null;
                if (issues == null) return;
                e.Handled = true;

                var buffer = _host.TextView.TextBuffer;
                var line   = _pendingLine;
                _margin.VisualElement.Dispatcher.BeginInvoke(
                    new Action(() => OpenMenu(buffer, line, issues)),
                    System.Windows.Threading.DispatcherPriority.Input);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "WarningGlyph: glyph-margin mouse-up handling failed");
            }
        }

        private void OpenMenu(ITextBuffer buffer, int lineNumber, IReadOnlyList<CodeIssueInfo> issues)
        {
            try
            {
                Serilog.Log.Debug("WarningGlyph: opening fix menu for line {Line} ({Count} issue(s))",
                    lineNumber + 1, issues.Count);

                var openedAt = DateTime.UtcNow;
                var menu = WarningGlyphMenu.Build(buffer, issues);
                menu.PlacementTarget = _margin.VisualElement;
                menu.Placement       = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.Closed += (_, __) => Serilog.Log.Debug(
                    "WarningGlyph: fix menu closed after {Ms}ms",
                    (int)(DateTime.UtcNow - openedAt).TotalMilliseconds);
                menu.IsOpen = true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "WarningGlyph: opening the fix menu failed");
            }
        }

        private bool TryGetIssuesAt(MouseButtonEventArgs e, out int lineNumber, out IReadOnlyList<CodeIssueInfo> issues)
        {
            lineNumber = -1;
            issues = Array.Empty<CodeIssueInfo>();

            var view = _host.TextView;
            if (view.IsClosed || view.InLayout) return false;

            var y = e.GetPosition(view.VisualElement).Y + view.ViewportTop;
            var viewLine = view.TextViewLines.GetTextViewLineContainingYCoordinate(y);
            if (viewLine == null) return false;

            lineNumber = viewLine.Start.GetContainingLine().LineNumber;

            if (!view.TextBuffer.Properties.TryGetProperty(typeof(AnalysisController), out AnalysisController controller))
                return false;

            var byLine = WarningGlyphLineIndex.GroupByLine(
                controller.CurrentIssues, view.TextBuffer.CurrentSnapshot.LineCount);
            if (!byLine.TryGetValue(lineNumber, out var lineIssues)) return false;

            issues = lineIssues;
            return true;
        }
    }
}
