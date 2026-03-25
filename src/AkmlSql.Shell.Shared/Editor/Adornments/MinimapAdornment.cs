#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Config;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Adornments
{
    /// <summary>
    /// T096: A compact minimap overview displayed in the right margin of the SQL editor.
    /// Shows a scaled-down rendering of the entire document with a highlighted viewport rectangle.
    /// Clicking or dragging scrolls the editor to the corresponding position.
    /// </summary>
    internal sealed class MinimapAdornment
    {
        private const string AdornmentLayerName = "AkmlSqlMinimap";
        private const double MinimapWidth = 80;
        private const double MinFontSize = 1.5;

        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private readonly Canvas _minimapCanvas;
        private readonly Border _viewportHighlight;
        private bool _enabled;
        private bool _isDragging;

        public MinimapAdornment(IWpfTextView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _layer = view.GetAdornmentLayer(AdornmentLayerName);

            try
            {
                var settings = ConfigManager.Load();
                _enabled = settings.EditorProductivity.Minimap;
            }
            catch
            {
                _enabled = false; // Minimap is off by default
            }

            _minimapCanvas = new Canvas
            {
                Width = MinimapWidth,
                Background = new SolidColorBrush(Color.FromArgb(220, 248, 248, 250)),
                ClipToBounds = true
            };

            _viewportHighlight = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 100, 140, 200)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 100, 140, 200)),
                BorderThickness = new Thickness(1),
                Width = MinimapWidth
            };

            _minimapCanvas.MouseLeftButtonDown += OnMinimapMouseDown;
            _minimapCanvas.MouseMove += OnMinimapMouseMove;
            _minimapCanvas.MouseLeftButtonUp += OnMinimapMouseUp;

            _view.LayoutChanged += OnLayoutChanged;
            _view.Closed += OnViewClosed;

            Log.Debug("MinimapAdornment: created (enabled={Enabled})", _enabled);
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (!_enabled) return;

            try
            {
                RenderMinimap();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "MinimapAdornment: layout update failed");
            }
        }

        private void RenderMinimap()
        {
            _layer.RemoveAllAdornments();
            _minimapCanvas.Children.Clear();

            var snapshot = _view.TextSnapshot;
            var totalLines = snapshot.LineCount;
            if (totalLines == 0) return;

            var viewportHeight = _view.ViewportHeight;
            _minimapCanvas.Height = viewportHeight;

            var lineHeight = Math.Max(1.0, viewportHeight / totalLines);
            var fontSize = Math.Max(MinFontSize, lineHeight * 0.8);

            // Render visible line indicators (simplified — just draw colored bars per line)
            for (int i = 0; i < totalLines && (i * lineHeight) < viewportHeight; i++)
            {
                var line = snapshot.GetLineFromLineNumber(i);
                var text = line.GetText();
                if (string.IsNullOrWhiteSpace(text)) continue;

                var textWidth = Math.Min(text.TrimEnd().Length * (fontSize * 0.5), MinimapWidth - 2);

                var lineBar = new Border
                {
                    Width = Math.Max(2, textWidth),
                    Height = Math.Max(1, lineHeight - 0.5),
                    Background = new SolidColorBrush(Color.FromArgb(120, 80, 80, 100))
                };

                Canvas.SetLeft(lineBar, 2);
                Canvas.SetTop(lineBar, i * lineHeight);
                _minimapCanvas.Children.Add(lineBar);
            }

            // Viewport highlight
            var firstVisibleLine = 0;
            var lastVisibleLine = totalLines - 1;

            if (_view.TextViewLines != null)
            {
                var first = _view.TextViewLines.FirstVisibleLine;
                var last = _view.TextViewLines.LastVisibleLine;
                if (first != null)
                    firstVisibleLine = snapshot.GetLineNumberFromPosition(first.Start.Position);
                if (last != null)
                    lastVisibleLine = snapshot.GetLineNumberFromPosition(last.Start.Position);
            }

            var vpTop = firstVisibleLine * lineHeight;
            var vpHeight = Math.Max(10, (lastVisibleLine - firstVisibleLine + 1) * lineHeight);

            _viewportHighlight.Height = vpHeight;
            Canvas.SetTop(_viewportHighlight, vpTop);
            Canvas.SetLeft(_viewportHighlight, 0);

            if (!_minimapCanvas.Children.Contains(_viewportHighlight))
                _minimapCanvas.Children.Add(_viewportHighlight);

            // Position the minimap on the right side of the viewport
            Canvas.SetLeft(_minimapCanvas, _view.ViewportRight - MinimapWidth);
            Canvas.SetTop(_minimapCanvas, _view.ViewportTop);

            _layer.AddAdornment(
                AdornmentPositioningBehavior.ViewportRelative,
                null,
                null,
                _minimapCanvas,
                null);
        }

        private void OnMinimapMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _minimapCanvas.CaptureMouse();
            ScrollToMinimapPosition(e.GetPosition(_minimapCanvas).Y);
        }

        private void OnMinimapMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            ScrollToMinimapPosition(e.GetPosition(_minimapCanvas).Y);
        }

        private void OnMinimapMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _minimapCanvas.ReleaseMouseCapture();
        }

        private void ScrollToMinimapPosition(double y)
        {
            try
            {
                var totalLines = _view.TextSnapshot.LineCount;
                if (totalLines == 0) return;

                var lineHeight = _minimapCanvas.Height / totalLines;
                var targetLine = (int)(y / lineHeight);
                targetLine = Math.Max(0, Math.Min(targetLine, totalLines - 1));

                var targetSnapshotLine = _view.TextSnapshot.GetLineFromLineNumber(targetLine);
                _view.DisplayTextLineContainingBufferPosition(
                    targetSnapshotLine.Start,
                    0,
                    ViewRelativePosition.Top);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "MinimapAdornment: scroll failed");
            }
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            _view.LayoutChanged -= OnLayoutChanged;
            _view.Closed -= OnViewClosed;
        }
    }
}
