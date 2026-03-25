#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Core.Config;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Adornments
{
    /// <summary>
    /// T094: MEF-managed adornment that pins SQL scope context (BEGIN/END, IF/ELSE, WHILE,
    /// stored procedure/function headers) at the top of the editor when the user scrolls
    /// past them. Similar to VS Code's "sticky scroll" feature.
    /// </summary>
    internal sealed class StickyScrollAdornment
    {
        private const string AdornmentLayerName = "AkmlSqlStickyScroll";
        private const int MaxStickyLines = 5;

        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private readonly StackPanel _stickyPanel;
        private bool _enabled;

        // Patterns for scope-opening keywords
        private static readonly Regex ScopePattern = new Regex(
            @"^\s*(CREATE\s+(PROCEDURE|FUNCTION|TRIGGER|VIEW)|BEGIN(\s+TRY|\s+CATCH)?|IF\b|ELSE\b|WHILE\b|" +
            @"WITH\s+\w+\s+AS|MERGE\b|BEGIN\s+TRANSACTION)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public StickyScrollAdornment(IWpfTextView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _layer = view.GetAdornmentLayer(AdornmentLayerName);

            _stickyPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Background = new SolidColorBrush(Color.FromArgb(240, 245, 245, 248)),
                Opacity = 0.95
            };

            try
            {
                var settings = ConfigManager.Load();
                _enabled = settings.EditorProductivity.StickyScroll;
            }
            catch
            {
                _enabled = true;
            }

            _view.LayoutChanged += OnLayoutChanged;
            _view.Closed += OnViewClosed;

            Log.Debug("StickyScrollAdornment: created (enabled={Enabled})", _enabled);
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (!_enabled) return;

            try
            {
                UpdateStickyHeaders();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "StickyScrollAdornment: layout update failed");
            }
        }

        private void UpdateStickyHeaders()
        {
            _layer.RemoveAllAdornments();
            _stickyPanel.Children.Clear();

            var snapshot = _view.TextSnapshot;
            var firstVisibleLine = _view.TextViewLines?.FirstVisibleLine;
            if (firstVisibleLine == null) return;

            var firstVisibleLineNumber = snapshot.GetLineNumberFromPosition(
                firstVisibleLine.Start.Position);

            if (firstVisibleLineNumber <= 0) return;

            // Walk backwards from the first visible line to find scope-opening lines
            var scopeHeaders = new List<string>();

            int indentLevel = int.MaxValue;
            for (int lineNum = firstVisibleLineNumber - 1; lineNum >= 0 && scopeHeaders.Count < MaxStickyLines; lineNum--)
            {
                var line = snapshot.GetLineFromLineNumber(lineNum);
                var lineText = line.GetText();

                if (string.IsNullOrWhiteSpace(lineText)) continue;

                var currentIndent = GetIndentLevel(lineText);

                // Only show scope headers that are at a lower indent level than what we've seen
                if (currentIndent < indentLevel && ScopePattern.IsMatch(lineText))
                {
                    scopeHeaders.Insert(0, lineText.TrimEnd());
                    indentLevel = currentIndent;
                }
            }

            if (scopeHeaders.Count == 0) return;

            // Build the sticky panel
            foreach (var header in scopeHeaders)
            {
                var textBlock = new TextBlock
                {
                    Text = header,
                    FontFamily = _view.FormattedLineSource?.DefaultTextProperties?.Typeface?.FontFamily
                                 ?? new FontFamily("Consolas"),
                    FontSize = (_view.FormattedLineSource?.DefaultTextProperties?.FontRenderingEmSize ?? 13.0) * 0.9,
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 90)),
                    Padding = new Thickness(4, 1, 4, 1),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = _view.ViewportWidth
                };

                _stickyPanel.Children.Add(textBlock);
            }

            // Add a bottom border
            _stickyPanel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                Margin = new Thickness(0)
            });

            // Measure and position
            _stickyPanel.Measure(new Size(_view.ViewportWidth, double.PositiveInfinity));
            _stickyPanel.Width = _view.ViewportWidth;

            Canvas.SetLeft(_stickyPanel, _view.ViewportLeft);
            Canvas.SetTop(_stickyPanel, _view.ViewportTop);

            _layer.AddAdornment(
                AdornmentPositioningBehavior.ViewportRelative,
                null,
                null,
                _stickyPanel,
                null);
        }

        private static int GetIndentLevel(string line)
        {
            int spaces = 0;
            foreach (char c in line)
            {
                if (c == ' ') spaces++;
                else if (c == '\t') spaces += 4;
                else break;
            }
            return spaces;
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            _view.LayoutChanged -= OnLayoutChanged;
            _view.Closed -= OnViewClosed;
        }
    }
}
