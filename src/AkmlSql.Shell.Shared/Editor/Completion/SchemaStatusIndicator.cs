using System;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.Text.Editor;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// Bottom-right adornment showing schema loading progress.
    /// "⟳ Loading schema for {database}..." → "✓ {database} ready ({n} objects)" → hides.
    /// Chrome flows through <see cref="ThemeRegistry"/> attached to this Border.
    /// </summary>
    internal sealed class SchemaStatusIndicator : Border
    {
        private readonly TextBlock _text;
        private readonly IWpfTextView _textView;
        private System.Threading.Timer _hideTimer;

        public SchemaStatusIndicator(IWpfTextView textView)
        {
            _textView = textView;

            // Attach the registry so SetResourceReference resolves on us and our descendant.
            ThemeRegistry.Instance.AttachTo(this);

            _text = new TextBlock
            {
                FontSize = Typography.Small,
                Padding  = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs)
            };
            _text.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            SetResourceReference(BackgroundProperty, ThemeTokens.EditorMarginBackground);
            CornerRadius = new CornerRadius(3, 0, 0, 0);
            Child        = _text;
            Visibility   = Visibility.Collapsed;

            _textView.LayoutChanged += (s, e) => Reposition();
        }

        public void ShowLoading(string database)
        {
            _text.Text = $"⟳ Loading schema for {database}...";
            Visibility = Visibility.Visible;
            Reposition();
        }

        public void ShowReady(string database, int objectCount)
        {
            _text.Text = $"✓ {database} ready ({objectCount} objects)";
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
    }
}
