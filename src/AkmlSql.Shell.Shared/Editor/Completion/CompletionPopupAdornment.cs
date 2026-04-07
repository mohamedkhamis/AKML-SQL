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
        private readonly WildcardExpansionPopup _wildcardContent;
        private readonly Popup _wildcardPopup;
        private readonly ObjectDefinitionPanel _definitionPanel;
        private readonly Popup _definitionPopup;

        public AkmlCompletionPopup Popup => _popupContent;
        public WildcardExpansionPopup WildcardPopup => _wildcardContent;
        public ObjectDefinitionPanel DefinitionPanel => _definitionPanel;

        /// <summary>
        /// Controls the opacity of the completion popup.
        /// Used to make the popup semi-transparent while Ctrl is held down.
        /// </summary>
        public double PopupOpacity
        {
            get => _popup?.Opacity ?? 1.0;
            set
            {
                if (_popup != null) _popup.Opacity = value;
                if (_definitionPopup != null) _definitionPopup.Opacity = value;
            }
        }

        /// <summary>True if the main completion popup is currently showing.</summary>
        public bool IsPopupVisible => _popup.IsOpen && _popupContent.IsOpen;

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

            // Wildcard expansion popup (checkbox list)
            _wildcardContent = new WildcardExpansionPopup();
            _wildcardPopup = new Popup
            {
                Child = _wildcardContent,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.None,
                Placement = PlacementMode.Custom,
                CustomPopupPlacementCallback = PlacePopup,
                StaysOpen = true,
                Focusable = false,
                IsOpen = false
            };
            _wildcardContent.Visibility = Visibility.Visible;

            // Object Definition panel (shows beside the completion popup)
            _definitionPanel = new ObjectDefinitionPanel();
            _definitionPopup = new Popup
            {
                Child = _definitionPanel,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.None,
                Placement = PlacementMode.Custom,
                CustomPopupPlacementCallback = PlaceDefinitionPopup,
                StaysOpen = true,
                Focusable = false,
                IsOpen = false
            };
            _definitionPanel.Visibility = Visibility.Visible;

            _textView.LayoutChanged += OnLayoutChanged;
            _textView.Closed += OnClosed;
            _textView.LostAggregateFocus += (s, e) => { Hide(); HideWildcard(); };
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
            _popup.Opacity = 1.0;
            _popup.IsOpen = false;
            _popupContent.Hide();
            HideDefinition();
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

        /// <summary>Show the wildcard expansion popup at the current caret position.</summary>
        public void ShowWildcard()
        {
            _wildcardPopup.PlacementTarget = _textView.VisualElement;
            _wildcardPopup.IsOpen = true;
        }

        /// <summary>Hide the wildcard expansion popup.</summary>
        public void HideWildcard()
        {
            _wildcardPopup.IsOpen = false;
            _wildcardContent.Hide();
        }

        /// <summary>Reposition the wildcard popup at the current caret.</summary>
        public void RepositionWildcard()
        {
            if (_wildcardPopup.IsOpen)
            {
                _wildcardPopup.HorizontalOffset += 0.01;
                _wildcardPopup.HorizontalOffset -= 0.01;
            }
        }

        /// <summary>True if the wildcard expansion popup is currently showing.</summary>
        public bool IsWildcardOpen
        {
            get { return _wildcardPopup.IsOpen && _wildcardContent.IsOpen; }
        }

        /// <summary>Show the object definition panel to the right of the completion popup.</summary>
        public void ShowDefinition()
        {
            if (!_definitionPanel.HasContent) return;
            _definitionPopup.PlacementTarget = _textView.VisualElement;
            _definitionPopup.IsOpen = true;
        }

        /// <summary>Hide the object definition panel.</summary>
        public void HideDefinition()
        {
            _definitionPopup.IsOpen = false;
            _definitionPanel.Clear();
        }

        /// <summary>Reposition the object definition panel.</summary>
        public void RepositionDefinition()
        {
            if (_definitionPopup.IsOpen)
            {
                _definitionPopup.HorizontalOffset += 0.01;
                _definitionPopup.HorizontalOffset -= 0.01;
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

        /// <summary>
        /// Places the definition popup to the right of the completion popup.
        /// Falls back to left side if there is not enough space on the right.
        /// </summary>
        private CustomPopupPlacement[] PlaceDefinitionPopup(Size popupSize, Size targetSize, Point offset)
        {
            try
            {
                var caretPos = _textView.Caret.Position.BufferPosition;
                var caretLine = _textView.GetTextViewLineContainingBufferPosition(caretPos);
                if (caretLine == null)
                    return new[] { new CustomPopupPlacement(new Point(0, 0), PopupPrimaryAxis.Vertical) };

                // Calculate same left/top as the main popup
                var wordStart = FindWordStart(caretPos);
                var wordLine = _textView.GetTextViewLineContainingBufferPosition(wordStart);

                double popupLeft;
                if (wordLine != null)
                {
                    var bounds = wordLine.GetCharacterBounds(wordStart);
                    popupLeft = bounds.Left - _textView.ViewportLeft;
                }
                else
                {
                    popupLeft = _textView.Caret.Left - _textView.ViewportLeft;
                }
                popupLeft = Math.Max(0, popupLeft);

                double top = caretLine.Bottom - _textView.ViewportTop + 2;
                if (top + popupSize.Height > _textView.ViewportHeight)
                {
                    top = caretLine.Top - _textView.ViewportTop - popupSize.Height - 2;
                }
                top = Math.Max(0, top);

                // Position to the right of the completion popup (actual width + 2px gap)
                double completionWidth = _popupContent.ActualWidth > 0 ? _popupContent.ActualWidth : 380;
                double left = popupLeft + completionWidth + 2;

                // If it would exceed the viewport, place it to the left of the popup
                if (left + popupSize.Width > _textView.ViewportWidth)
                {
                    left = popupLeft - popupSize.Width - 2;
                    if (left < 0) left = 0;
                }

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
            if (_wildcardPopup.IsOpen)
                RepositionWildcard();
            if (_definitionPopup.IsOpen)
                RepositionDefinition();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.Closed -= OnClosed;
            Hide();
            HideWildcard();
            HideDefinition();
        }
    }
}
