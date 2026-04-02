using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// Bottom-right adornment showing schema loading progress.
    /// "⟳ Loading schema for {database}..." → "✓ {database} ready ({n} objects)" → hides.
    /// </summary>
    internal sealed class SchemaStatusIndicator : Border
    {
        private readonly TextBlock _text;
        private readonly IWpfTextView _textView;
        private System.Threading.Timer _hideTimer;

        private static readonly SolidColorBrush BgBrush =
            Freeze(new SolidColorBrush(Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E)));
        private static readonly SolidColorBrush TextColor =
            Freeze(new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)));

        public SchemaStatusIndicator(IWpfTextView textView)
        {
            _textView = textView;

            _text = new TextBlock
            {
                Foreground = TextColor,
                FontSize = 11,
                Padding = new Thickness(8, 4, 8, 4)
            };

            Background = BgBrush;
            CornerRadius = new CornerRadius(3, 0, 0, 0);
            Child = _text;
            Visibility = Visibility.Collapsed;

            _textView.LayoutChanged += (s, e) => Reposition();
        }

        public void ShowLoading(string database)
        {
            _text.Text = $"\u27F3 Loading schema for {database}...";
            Visibility = Visibility.Visible;
            Reposition();
        }

        public void ShowReady(string database, int objectCount)
        {
            _text.Text = $"\u2713 {database} ready ({objectCount} objects)";
            Visibility = Visibility.Visible;
            Reposition();

            // Auto-hide after 3 seconds
            _hideTimer?.Dispose();
            _hideTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    _textView.VisualElement.Dispatcher.Invoke(() => Visibility = Visibility.Collapsed);
                }
                catch { }
            }, null, 3000, System.Threading.Timeout.Infinite);
        }

        public void Hide()
        {
            Visibility = Visibility.Collapsed;
            _hideTimer?.Dispose();
        }

        private void Reposition()
        {
            if (Visibility != Visibility.Visible) return;
            try
            {
                Canvas.SetLeft(this, _textView.ViewportWidth - ActualWidth - 4);
                Canvas.SetTop(this, _textView.ViewportHeight - ActualHeight - 4);
            }
            catch { }
        }

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
