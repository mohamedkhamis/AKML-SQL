using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// Manages the AkmlCompletionPopup's lifecycle on a text view.
    /// Positions below the caret, flips above if near the bottom of the editor.
    /// Repositions on scroll and layout changes.
    /// </summary>
    internal sealed class CompletionPopupAdornment
    {
        private readonly IWpfTextView _textView;
        private readonly IAdornmentLayer _adornmentLayer;
        private readonly AkmlCompletionPopup _popup;
        private bool _isAdded;

        public AkmlCompletionPopup Popup => _popup;

        public CompletionPopupAdornment(IWpfTextView textView, IAdornmentLayer adornmentLayer)
        {
            _textView = textView;
            _adornmentLayer = adornmentLayer;
            _popup = new AkmlCompletionPopup();

            _textView.LayoutChanged += OnLayoutChanged;
            _textView.Closed += OnClosed;
        }

        /// <summary>Show the popup at the current caret position.</summary>
        public void Show()
        {
            EnsureAdded();
            PositionAtCaret();
            _popup.Show();
        }

        /// <summary>Hide the popup.</summary>
        public void Hide()
        {
            _popup.Hide();
        }

        /// <summary>Reposition the popup at the current caret.</summary>
        public void Reposition()
        {
            if (_popup.IsOpen)
                PositionAtCaret();
        }

        private void PositionAtCaret()
        {
            try
            {
                var caretPos = _textView.Caret.Position.BufferPosition;
                var caretLine = _textView.GetTextViewLineContainingBufferPosition(caretPos);
                if (caretLine == null) return;

                // Find the start of the current word for left alignment
                var wordStart = FindWordStart(caretPos);
                var wordLine = _textView.GetTextViewLineContainingBufferPosition(wordStart);

                double left;
                if (wordLine != null)
                {
                    var bounds = wordLine.GetCharacterBounds(wordStart);
                    left = bounds.Left - _textView.ViewportLeft;
                }
                else
                {
                    left = _textView.Caret.Left - _textView.ViewportLeft;
                }

                // Position below caret line
                double top = caretLine.Bottom - _textView.ViewportTop + 2;

                // Flip above if near bottom of editor
                double popupHeight = Math.Min(15 * 22 + 30, _popup.ActualHeight > 0 ? _popup.ActualHeight : 360);
                if (top + popupHeight > _textView.ViewportHeight)
                {
                    top = caretLine.Top - _textView.ViewportTop - popupHeight - 2;
                }

                // Clamp to viewport
                left = Math.Max(0, Math.Min(left, _textView.ViewportWidth - 380));
                top = Math.Max(0, top);

                Canvas.SetLeft(_popup, left);
                Canvas.SetTop(_popup, top);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to position completion popup");
            }
        }

        private SnapshotPoint FindWordStart(SnapshotPoint point)
        {
            var snapshot = point.Snapshot;
            int pos = point.Position;
            while (pos > 0)
            {
                char c = snapshot[pos - 1];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@')
                    pos--;
                else
                    break;
            }
            return new SnapshotPoint(snapshot, pos);
        }

        private void EnsureAdded()
        {
            if (_isAdded) return;
            try
            {
                _adornmentLayer.AddAdornment(
                    AdornmentPositioningBehavior.ViewportRelative,
                    null, null, _popup, null);
                _isAdded = true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to add completion popup adornment");
            }
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (_popup.IsOpen)
                PositionAtCaret();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.Closed -= OnClosed;
            _popup.Hide();
        }
    }
}
