using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// Manages the AkmlCompletionPopup using a WPF Popup (top-level window).
    /// This ensures our popup renders ABOVE SSMS's native IntelliSense which
    /// also uses a top-level window. The Popup doesn't steal focus from the editor.
    /// </summary>
    internal sealed class CompletionPopupAdornment
    {
        private readonly IWpfTextView _textView;
        private readonly AkmlCompletionPopup _popupContent;
        private readonly Popup _popup;

        public AkmlCompletionPopup Popup => _popupContent;

        public CompletionPopupAdornment(IWpfTextView textView, IAdornmentLayer adornmentLayer)
        {
            _textView = textView;
            _popupContent = new AkmlCompletionPopup();

            // Use WPF Popup for top-level rendering (above SSMS native IntelliSense)
            _popup = new Popup
            {
                Child = _popupContent,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.None,
                Placement = PlacementMode.Custom,
                CustomPopupPlacementCallback = PlacePopup,
                StaysOpen = true,  // We control dismissal ourselves
                Focusable = false,
                IsOpen = false
            };

            // Make the popup content always visible (we control show/hide via IsOpen)
            _popupContent.Visibility = Visibility.Visible;

            _textView.LayoutChanged += OnLayoutChanged;
            _textView.Closed += OnClosed;
            _textView.LostAggregateFocus += (s, e) => Hide();
        }

        /// <summary>Show the popup at the current caret position.</summary>
        public void Show()
        {
            _popup.PlacementTarget = _textView.VisualElement;
            _popup.IsOpen = true;
        }

        /// <summary>Hide the popup.</summary>
        public void Hide()
        {
            _popup.IsOpen = false;
            _popupContent.Hide();
        }

        /// <summary>Reposition the popup at the current caret.</summary>
        public void Reposition()
        {
            if (_popup.IsOpen)
            {
                // Force WPF to recalculate position
                _popup.HorizontalOffset += 0.01;
                _popup.HorizontalOffset -= 0.01;
            }
        }

        private CustomPopupPlacement[] PlacePopup(Size popupSize, Size targetSize, Point offset)
        {
            try
            {
                var caretPos = _textView.Caret.Position.BufferPosition;
                var caretLine = _textView.GetTextViewLineContainingBufferPosition(caretPos);
                if (caretLine == null) return new[] { new CustomPopupPlacement(new Point(0, 0), PopupPrimaryAxis.Vertical) };

                // Find word start for left alignment
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

                // Below caret line
                double top = caretLine.Bottom - _textView.ViewportTop + 2;

                // Flip above if near bottom
                if (top + popupSize.Height > _textView.ViewportHeight)
                {
                    top = caretLine.Top - _textView.ViewportTop - popupSize.Height - 2;
                }

                left = Math.Max(0, left);
                top = Math.Max(0, top);

                return new[] { new CustomPopupPlacement(new Point(left, top), PopupPrimaryAxis.Vertical) };
            }
            catch
            {
                return new[] { new CustomPopupPlacement(new Point(0, 0), PopupPrimaryAxis.Vertical) };
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

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (_popup.IsOpen)
                Reposition();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.Closed -= OnClosed;
            Hide();
        }
    }
}
