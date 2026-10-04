#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Text;
using AkmlSql.Shell.Shared.Ui.Theme;
using Typography = AkmlSql.Shell.Shared.Ui.Theme.Typography;

namespace AkmlSql.Shell.Shared.Ui.SqlPreview
{
    /// <summary>
    /// Spec 040 (research R20) — the one read-only SQL preview used by SQL History, the Format
    /// Styles window and the History compare window: selectable text (not a TextBlock), syntax
    /// colours from live theme tokens, tab stops at the style's width, search-term and changed-line
    /// highlights, a line-number gutter, and a notice instead of rendering megabytes of text.
    /// </summary>
    /// <remarks>
    /// One <see cref="Paragraph"/> per source line, so a highlighted line's background spans the
    /// full width and the gutter lines up. Wrapping is off (the document is as wide as its longest
    /// line) and one outer <see cref="ScrollViewer"/> scrolls the gutter and the text together.
    /// </remarks>
    public sealed class SqlPreviewView : UserControl
    {
        private static readonly IReadOnlyList<string> NoTerms = new string[0];
        private static readonly IReadOnlyCollection<int> NoLines = new int[0];
        private static readonly double LineHeight = Math.Round(Typography.Body * 1.4);

        /// <summary>Extra page width beyond the longest line: the caret and the text box's padding.</summary>
        private const double PageSlack = 12;

        /// <summary>How much wider than measured the page is, so the longest line never wraps.</summary>
        private const double PageMargin = 1.15;

        private readonly ScrollViewer _scroll;
        private readonly TextBlock _gutter;
        private readonly RichTextBox _box;
        private readonly FlowDocument _document;

        private string _text = string.Empty;
        private int _tabSize = 4;
        private IReadOnlyList<string> _highlightTerms = NoTerms;
        private IReadOnlyCollection<int> _highlightLines = NoLines;
        private bool _showLineNumbers = true;
        private int _maxDisplayChars = 262144;

        public SqlPreviewView()
        {
            ThemeRegistry.Instance.AttachTo(this);
            Background = Brushes.Transparent;

            _gutter = new TextBlock
            {
                FontFamily = Typography.MonoFont,
                FontSize = Typography.Body,
                LineHeight = LineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(Spacing.Sm, 0, Spacing.Sm, 0),
                MinWidth = 16,
            };
            _gutter.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            _document = new FlowDocument
            {
                FontFamily = Typography.MonoFont,
                FontSize = Typography.Body,
                LineHeight = LineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                PagePadding = new Thickness(0),
            };
            _document.SetResourceReference(FlowDocument.ForegroundProperty, ThemeTokens.TextPrimary);

            _box = new RichTextBox(_document)
            {
                IsReadOnly = true,
                IsDocumentEnabled = true,
                IsReadOnlyCaretVisible = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FontFamily = Typography.MonoFont,
                FontSize = Typography.Body,
            };
            _box.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            // The text box answers to the name given to the whole preview (e.g. "Preview").
            _box.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new System.Windows.Data.Binding
            {
                Path = new PropertyPath(System.Windows.Automation.AutomationProperties.NameProperty),
                Source = this,
            });
            _box.ContextMenu = BuildContextMenu();
            // The inner ScrollViewer of a RichTextBox swallows the wheel even when it can't scroll;
            // hand it to the outer viewer that owns both the gutter and the text.
            _box.PreviewMouseWheel += OnPreviewMouseWheel;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_gutter, 0);
            Grid.SetColumn(_box, 1);
            grid.Children.Add(_gutter);
            grid.Children.Add(_box);

            _scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = grid,
            };
            Content = _scroll;

            Render();
        }

        /// <summary>The SQL to show. Tabs are expanded for display only; "Copy all" copies this text.</summary>
        public string Text
        {
            get => _text;
            set { _text = value ?? string.Empty; Render(); }
        }

        /// <summary>Spaces per tab stop (the style's tab width). Clamped to 1–16.</summary>
        public int TabSize
        {
            get => _tabSize;
            set { _tabSize = Math.Max(1, Math.Min(16, value)); Render(); }
        }

        /// <summary>Terms highlighted case-insensitively with the search-match background.</summary>
        public IReadOnlyList<string> HighlightTerms
        {
            get => _highlightTerms;
            set { _highlightTerms = value ?? NoTerms; Render(); }
        }

        /// <summary>0-based lines given the selection background (the lines a style change moved).</summary>
        public IReadOnlyCollection<int> HighlightLines
        {
            get => _highlightLines;
            set { _highlightLines = value ?? NoLines; ApplyLineHighlights(); }
        }

        public bool ShowLineNumbers
        {
            get => _showLineNumbers;
            set { _showLineNumbers = value; _gutter.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
        }

        /// <summary>Characters rendered before the "showing the first …" notice replaces the rest.</summary>
        public int MaxDisplayChars
        {
            get => _maxDisplayChars;
            set { _maxDisplayChars = Math.Max(1, value); Render(); }
        }

        /// <summary>Sets the text and the search terms with one render.</summary>
        public void Show(string? text, IReadOnlyList<string>? highlightTerms)
        {
            _text = text ?? string.Empty;
            _highlightTerms = highlightTerms ?? NoTerms;
            Render();
        }

        internal IReadOnlyList<Paragraph> Lines => _document.Blocks.OfType<Paragraph>().ToList();

        internal TextBlock Gutter => _gutter;

        /// <summary>The viewer that scrolls the gutter and the text (the compare window keeps two in step).</summary>
        internal ScrollViewer Scroller => _scroll;

        private IReadOnlyList<int?>? _gutterNumbers;
        private IReadOnlyDictionary<int, string>? _lineTints;

        /// <summary>
        /// Spec 040 (HIS-10): the gutter's number for each line (null leaves it blank), instead of
        /// 1…n — the compare window pads each side with blank filler lines.
        /// </summary>
        internal IReadOnlyList<int?>? GutterNumbers
        {
            get => _gutterNumbers;
            set { _gutterNumbers = value; Render(); }
        }

        /// <summary>
        /// Spec 040 (HIS-10): 0-based line → theme brush key, drawn as a faint tint of that colour
        /// (added, removed and changed lines in the compare window).
        /// </summary>
        internal IReadOnlyDictionary<int, string>? LineTints
        {
            get => _lineTints;
            set { _lineTints = value; ApplyLineHighlights(); }
        }

        internal const double LineTintOpacity = 0.22;

        /// <summary>
        /// Column-aware tab expansion: a tab advances to the next multiple of
        /// <paramref name="tabSize"/> within its line; columns restart after every line break.
        /// </summary>
        internal static string ExpandTabs(string text, int tabSize)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\t') < 0) return text ?? string.Empty;
            if (tabSize < 1) tabSize = 1;

            var sb = new StringBuilder(text.Length + 16);
            int column = 0;
            foreach (char c in text)
            {
                if (c == '\t')
                {
                    int spaces = tabSize - (column % tabSize);
                    sb.Append(' ', spaces);
                    column += spaces;
                }
                else
                {
                    sb.Append(c);
                    column = (c == '\n' || c == '\r') ? 0 : column + 1;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Case-insensitive match regions for <paramref name="terms"/>, sorted and merged so no two
        /// overlap.
        /// </summary>
        internal static List<(int Start, int Length)> FindHighlightRegions(string text, IReadOnlyList<string> terms)
        {
            var regions = new List<(int Start, int Length)>();
            foreach (var term in terms)
            {
                if (string.IsNullOrEmpty(term)) continue;
                int pos = 0;
                while (pos < text.Length)
                {
                    int at = text.IndexOf(term, pos, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) break;
                    regions.Add((at, term.Length));
                    pos = at + 1;
                }
            }
            if (regions.Count == 0) return regions;

            regions.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length));
            var merged = new List<(int Start, int Length)> { regions[0] };
            for (int i = 1; i < regions.Count; i++)
            {
                var last = merged[merged.Count - 1];
                var current = regions[i];
                if (current.Start <= last.Start + last.Length)
                {
                    int end = Math.Max(last.Start + last.Length, current.Start + current.Length);
                    merged[merged.Count - 1] = (last.Start, end - last.Start);
                }
                else
                {
                    merged.Add(current);
                }
            }
            return merged;
        }

        private void Render()
        {
            _document.Blocks.Clear();

            if (_text.Length == 0)
            {
                _gutter.Text = string.Empty;
                return;
            }

            bool truncated = _text.Length > _maxDisplayChars;
            string body = ExpandTabs(truncated ? _text.Substring(0, _maxDisplayChars) : _text, _tabSize);

            var tokens = SqlPreviewTokenizer.Tokenize(body);
            var regions = _highlightTerms.Count > 0 ? FindHighlightRegions(body, _highlightTerms) : new List<(int, int)>();

            var line = NewLine();
            int longest = 0, current = 0;

            void Emit(string piece, string kind, bool highlighted)
            {
                int start = 0;
                while (start <= piece.Length)
                {
                    int nl = piece.IndexOf('\n', start);
                    string part = (nl < 0 ? piece.Substring(start) : piece.Substring(start, nl - start)).Replace("\r", string.Empty);
                    if (part.Length > 0)
                    {
                        line.Inlines.Add(MakeRun(part, kind, highlighted));
                        current += part.Length;
                    }
                    if (nl < 0) break;
                    longest = Math.Max(longest, current);
                    current = 0;
                    line = NewLine();
                    start = nl + 1;
                }
            }

            int regionIdx = 0;
            foreach (var token in tokens)
            {
                int spanEnd = token.Start + token.Length;
                int cursor = token.Start;
                while (regionIdx < regions.Count && regions[regionIdx].Item1 + regions[regionIdx].Item2 <= cursor) regionIdx++;

                int r = regionIdx;
                while (cursor < spanEnd)
                {
                    while (r < regions.Count && regions[r].Item1 + regions[r].Item2 <= cursor) r++;
                    if (r >= regions.Count || regions[r].Item1 >= spanEnd)
                    {
                        Emit(body.Substring(cursor, spanEnd - cursor), token.Kind, highlighted: false);
                        break;
                    }
                    int hlStart = Math.Max(regions[r].Item1, cursor);
                    int hlEnd = Math.Min(regions[r].Item1 + regions[r].Item2, spanEnd);
                    if (hlStart > cursor) Emit(body.Substring(cursor, hlStart - cursor), token.Kind, highlighted: false);
                    if (hlEnd > hlStart) Emit(body.Substring(hlStart, hlEnd - hlStart), token.Kind, highlighted: true);
                    cursor = hlEnd;
                }
            }
            longest = Math.Max(longest, current);

            int codeLines = _document.Blocks.Count;
            if (truncated)
            {
                var notice = NewLine();
                var run = new Run($"— Showing the first {_maxDisplayChars / 1024} KB. Open the query to see all of it.");
                run.SetResourceReference(TextElement.ForegroundProperty, ThemeTokens.TextSecondary);
                notice.Inlines.Add(run);
            }

            var numbers = new StringBuilder();
            for (int i = 1; i <= codeLines; i++)
            {
                if (i > 1) numbers.Append('\n');
                if (_gutterNumbers == null) numbers.Append(i.ToString(CultureInfo.InvariantCulture));
                else if (i - 1 < _gutterNumbers.Count && _gutterNumbers[i - 1] is int n) numbers.Append(n.ToString(CultureInfo.InvariantCulture));
            }
            if (truncated) numbers.Append('\n');
            _gutter.Text = numbers.ToString();

            // No wrapping: the page is as wide as the longest line, so the outer viewer scrolls. The
            // longest line is measured as SSMS draws it (its text formatting mode, its font) with a
            // margin: in SSMS a character came out ~12% wider than "M" measured here, so the last
            // word of the longest line wrapped and the gutter's numbers no longer lined up.
            _document.PageWidth = Math.Max(64, Math.Max(LineWidth(LongestLine(body)), longest * Math.Ceiling(CharWidth())) * PageMargin
                                               + Spacing.Lg + PageSlack);

            ApplyLineHighlights();
        }

        private Paragraph NewLine()
        {
            var p = new Paragraph { Margin = new Thickness(0) };
            _document.Blocks.Add(p);
            return p;
        }

        private static Run MakeRun(string text, string kind, bool highlighted)
        {
            var run = new Run(text);
            string fg =
                kind == SqlPreviewTokenizer.KindKeyword ? ThemeTokens.AccentPrimary :
                kind == SqlPreviewTokenizer.KindString ? ThemeTokens.StatusSuccess :
                kind == SqlPreviewTokenizer.KindComment ? ThemeTokens.TextSecondary :
                ThemeTokens.TextPrimary;
            run.SetResourceReference(TextElement.ForegroundProperty, fg);
            if (highlighted) run.SetResourceReference(TextElement.BackgroundProperty, ThemeTokens.HistoryMatchHighlight);
            return run;
        }

        private void ApplyLineHighlights()
        {
            Dictionary<string, Brush>? tints = null;
            int index = 0;
            foreach (var block in _document.Blocks)
            {
                if (_lineTints != null && _lineTints.TryGetValue(index, out var key))
                {
                    tints ??= new Dictionary<string, Brush>();
                    if (!tints.TryGetValue(key, out var tint)) tints[key] = tint = Tint(key);
                    block.Background = tint;
                }
                else if (_highlightLines.Contains(index))
                    block.SetResourceReference(TextElement.BackgroundProperty, ThemeTokens.SurfaceSelection);
                else
                    block.ClearValue(TextElement.BackgroundProperty);
                index++;
            }
        }

        /// <summary>The theme colour behind <paramref name="key"/>, faint, as a frozen brush.</summary>
        private Brush Tint(string key)
        {
            var color = TryFindResource(key) is SolidColorBrush b ? b.Color : Colors.Gray;
            var brush = new SolidColorBrush(color) { Opacity = LineTintOpacity };
            brush.Freeze();
            return brush;
        }

        /// <summary>The longest line of <paramref name="text"/> (by characters, tabs already expanded).</summary>
        internal static string LongestLine(string text)
        {
            string longest = string.Empty;
            foreach (var line in text.Split('\n'))
            {
                var l = line.Replace("\r", string.Empty);
                if (l.Length > longest.Length) longest = l;
            }
            return longest;
        }

        /// <summary>The width <paramref name="line"/> takes in this control's font and text formatting mode.</summary>
        private double LineWidth(string line)
        {
            if (line.Length == 0) return 0;
            double pixelsPerDip = 1.0;
            try { pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch (Exception) { }
            var formatted = new FormattedText(line, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Typography.MonoFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                Typography.Body, Brushes.Black, null, TextOptions.GetTextFormattingMode(this), pixelsPerDip);
            return formatted.WidthIncludingTrailingWhitespace;
        }

        private double CharWidth()
        {
            double pixelsPerDip = 1.0;
            try { pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch (Exception) { }
            var probe = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Typography.MonoFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                Typography.Body, Brushes.Black, pixelsPerDip);
            return probe.WidthIncludingTrailingWhitespace;
        }

        private ContextMenu BuildContextMenu()
        {
            var copy = new MenuItem { Header = "Copy", Command = ApplicationCommands.Copy, CommandTarget = _box };
            var copyAll = new MenuItem { Header = "Copy all" };
            copyAll.Click += (s, e) =>
            {
                try { Clipboard.SetText(_text); }
                catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy; the user can retry */ }
            };
            var menu = new ContextMenu();
            menu.Items.Add(copy);
            menu.Items.Add(copyAll);
            return menu;
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - e.Delta / 3.0);
        }
    }
}
