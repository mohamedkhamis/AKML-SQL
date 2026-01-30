using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AKML.SQL.Shared.Contracts;
using AKML.SQL.SSMS.UI.Settings;
using Serilog;

namespace AKML.SQL.SSMS.UI.Completion
{
    /// <summary>
    /// WPF Completion popup window for IntelliSense suggestions.
    /// Implements Story 3.1: WPF Suggestion Popup Window.
    /// </summary>
    public partial class CompletionPopup : Window, INotifyPropertyChanged
    {
        private readonly ILogger _logger;
        private readonly ObservableCollection<CompletionItemViewModel> _items;
        private readonly ObservableCollection<CompletionItemViewModel> _filteredItems;
        private CompletionItemViewModel _selectedItem;
        private string _filterText;
        private string _statusText;
        private bool _showStatusBar;
        private bool _isAnimating;

        /// <summary>
        /// Event raised when a completion item is committed.
        /// </summary>
        public event EventHandler<CompletionCommitEventArgs> ItemCommitted;

        /// <summary>
        /// Event raised when the popup is dismissed.
        /// </summary>
        public event EventHandler Dismissed;

        public CompletionPopup()
        {
            InitializeComponent();
            DataContext = this;

            _logger = Log.Logger;
            _items = new ObservableCollection<CompletionItemViewModel>();
            _filteredItems = new ObservableCollection<CompletionItemViewModel>();
            _filterText = string.Empty;
            _showStatusBar = true;

            // Apply settings
            ApplySettings();

            // Subscribe to settings changes
            if (SettingsService.Instance != null)
            {
                SettingsService.Instance.SettingChanged += OnSettingChanged;
            }

            _logger.Debug("[CompletionPopup] Initialized");
        }

        private void ApplySettings()
        {
            var settings = SettingsService.Instance?.Settings;
            if (settings == null) return;

            Width = settings.PopupWidth;
            MaxHeight = settings.PopupMaxHeight;
            // Opacity is handled during animation
        }

        private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
        {
            // Apply relevant setting changes
            if (e.SettingName == nameof(AkmlSettings.PopupWidth) && e.NewValue is int width)
            {
                Dispatcher.Invoke(() => Width = width);
            }
            else if (e.SettingName == nameof(AkmlSettings.PopupMaxHeight) && e.NewValue is int height)
            {
                Dispatcher.Invoke(() => MaxHeight = height);
            }
        }

        /// <summary>
        /// Gets the filtered items to display.
        /// </summary>
        public ObservableCollection<CompletionItemViewModel> Items => _filteredItems;

        /// <summary>
        /// Gets or sets the selected item.
        /// </summary>
        public CompletionItemViewModel SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != value)
                {
                    if (_selectedItem != null)
                        _selectedItem.IsSelected = false;

                    _selectedItem = value;

                    if (_selectedItem != null)
                        _selectedItem.IsSelected = true;

                    OnPropertyChanged();
                    EnsureSelectedItemVisible();
                }
            }
        }

        /// <summary>
        /// Gets the current filter text.
        /// </summary>
        public string FilterText
        {
            get => _filterText;
            private set
            {
                if (_filterText != value)
                {
                    _filterText = value;
                    OnPropertyChanged();
                    ApplyFilter();
                }
            }
        }

        /// <summary>
        /// Gets or sets the status bar text.
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the item count display text.
        /// </summary>
        public string ItemCountText => $"{_filteredItems.Count} of {_items.Count}";

        /// <summary>
        /// Gets or sets whether to show the status bar.
        /// </summary>
        public bool ShowStatusBar
        {
            get => _showStatusBar;
            set
            {
                if (_showStatusBar != value)
                {
                    _showStatusBar = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Sets the completion items to display.
        /// </summary>
        public void SetItems(IEnumerable<CompletionItem> items)
        {
            _items.Clear();
            _filteredItems.Clear();

            foreach (var item in items)
            {
                var vm = new CompletionItemViewModel(item);
                _items.Add(vm);
                _filteredItems.Add(vm);
            }

            OnPropertyChanged(nameof(ItemCountText));

            // Select first item or preselected item
            var preselected = _filteredItems.FirstOrDefault(i => i.Preselect);
            SelectedItem = preselected ?? _filteredItems.FirstOrDefault();

            _logger.Debug("[CompletionPopup] Set {Count} items", _items.Count);
        }

        /// <summary>
        /// Shows the popup at the specified screen position.
        /// </summary>
        public void ShowAt(Point screenPosition)
        {
            // Adjust position to stay within screen bounds
            var adjustedPosition = AdjustPositionForScreenBounds(screenPosition);

            Left = adjustedPosition.X;
            Top = adjustedPosition.Y;

            _logger.Debug("[CompletionPopup] Showing at ({X}, {Y})", adjustedPosition.X, adjustedPosition.Y);

            // Animate fade-in
            if (!_isAnimating)
            {
                Opacity = 0;
                Show();
                AnimateFadeIn();
            }
            else
            {
                Show();
            }
        }

        /// <summary>
        /// Hides the popup with animation.
        /// </summary>
        public new void Hide()
        {
            if (_isAnimating)
            {
                base.Hide();
                return;
            }

            AnimateFadeOut(() => base.Hide());
        }

        /// <summary>
        /// Dismisses the popup without committing.
        /// </summary>
        public void Dismiss()
        {
            _logger.Debug("[CompletionPopup] Dismissed");
            Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Commits the selected item.
        /// </summary>
        public void CommitSelected()
        {
            if (SelectedItem != null)
            {
                _logger.Information("[CompletionPopup] Committing: {Label}", SelectedItem.Label);

                ItemCommitted?.Invoke(this, new CompletionCommitEventArgs(
                    SelectedItem.InsertText,
                    SelectedItem.Item,
                    _filterText));

                Hide();
            }
        }

        /// <summary>
        /// Handles keyboard input for navigation.
        /// </summary>
        public bool HandleKeyDown(Key key, ModifierKeys modifiers)
        {
            switch (key)
            {
                case Key.Up:
                    SelectPrevious();
                    return true;

                case Key.Down:
                    SelectNext();
                    return true;

                case Key.PageUp:
                    SelectPageUp();
                    return true;

                case Key.PageDown:
                    SelectPageDown();
                    return true;

                case Key.Home:
                    if (modifiers == ModifierKeys.Control)
                    {
                        SelectFirst();
                        return true;
                    }
                    break;

                case Key.End:
                    if (modifiers == ModifierKeys.Control)
                    {
                        SelectLast();
                        return true;
                    }
                    break;

                case Key.Tab:
                case Key.Enter:
                    CommitSelected();
                    return true;

                case Key.Escape:
                    Dismiss();
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Updates the filter text based on typing.
        /// </summary>
        public void UpdateFilter(string text)
        {
            FilterText = text ?? string.Empty;
        }

        /// <summary>
        /// Handles backspace to reduce filter.
        /// </summary>
        public void HandleBackspace()
        {
            if (_filterText.Length > 0)
            {
                FilterText = _filterText.Substring(0, _filterText.Length - 1);
            }
        }

        /// <summary>
        /// Checks if a character should dismiss the popup.
        /// </summary>
        public bool ShouldDismissOnChar(char c)
        {
            // Dismiss on delimiters
            return c == ' ' || c == ';' || c == '(' || c == ')' ||
                   c == ',' || c == '.' || c == '\n' || c == '\r';
        }

        private void SelectNext()
        {
            if (_filteredItems.Count == 0) return;

            var currentIndex = _filteredItems.IndexOf(SelectedItem);
            var nextIndex = (currentIndex + 1) % _filteredItems.Count;
            SelectedItem = _filteredItems[nextIndex];
        }

        private void SelectPrevious()
        {
            if (_filteredItems.Count == 0) return;

            var currentIndex = _filteredItems.IndexOf(SelectedItem);
            var prevIndex = currentIndex <= 0 ? _filteredItems.Count - 1 : currentIndex - 1;
            SelectedItem = _filteredItems[prevIndex];
        }

        private void SelectPageDown()
        {
            if (_filteredItems.Count == 0) return;

            var currentIndex = _filteredItems.IndexOf(SelectedItem);
            var pageSize = GetVisibleItemCount();
            var newIndex = Math.Min(currentIndex + pageSize, _filteredItems.Count - 1);
            SelectedItem = _filteredItems[newIndex];
        }

        private void SelectPageUp()
        {
            if (_filteredItems.Count == 0) return;

            var currentIndex = _filteredItems.IndexOf(SelectedItem);
            var pageSize = GetVisibleItemCount();
            var newIndex = Math.Max(currentIndex - pageSize, 0);
            SelectedItem = _filteredItems[newIndex];
        }

        private void SelectFirst()
        {
            if (_filteredItems.Count > 0)
                SelectedItem = _filteredItems[0];
        }

        private void SelectLast()
        {
            if (_filteredItems.Count > 0)
                SelectedItem = _filteredItems[_filteredItems.Count - 1];
        }

        private int GetVisibleItemCount()
        {
            // Estimate based on item height and list height
            const int itemHeight = 24;
            return Math.Max(1, (int)(CompletionListBox.ActualHeight / itemHeight));
        }

        private void EnsureSelectedItemVisible()
        {
            if (SelectedItem != null && CompletionListBox != null)
            {
                CompletionListBox.ScrollIntoView(SelectedItem);
            }
        }

        private void ApplyFilter()
        {
            _filteredItems.Clear();

            if (string.IsNullOrEmpty(_filterText))
            {
                foreach (var item in _items)
                {
                    item.HighlightedLabel = item.Label;
                    _filteredItems.Add(item);
                }
            }
            else
            {
                var filterLower = _filterText.ToLowerInvariant();
                var enableFuzzyMatching = SettingsService.Instance?.Settings?.EnableFuzzyMatching ?? true;

                foreach (var item in _items)
                {
                    var filterTextLower = item.FilterText.ToLowerInvariant();

                    // Simple contains matching, with optional fuzzy matching
                    var matches = filterTextLower.Contains(filterLower) ||
                        (enableFuzzyMatching && FuzzyMatch(filterTextLower, filterLower));

                    if (matches)
                    {
                        item.HighlightedLabel = item.Label; // TODO: Add highlight markup
                        _filteredItems.Add(item);
                    }
                }

                // Sort by relevance
                var sorted = _filteredItems
                    .OrderByDescending(i => i.RelevanceScore)
                    .ThenBy(i => i.Label)
                    .ToList();

                _filteredItems.Clear();
                foreach (var item in sorted)
                {
                    _filteredItems.Add(item);
                }
            }

            OnPropertyChanged(nameof(ItemCountText));

            // Update selection
            if (!_filteredItems.Contains(SelectedItem))
            {
                SelectedItem = _filteredItems.FirstOrDefault();
            }

            // Dismiss if no items match
            if (_filteredItems.Count == 0)
            {
                StatusText = "No matches";
            }
            else
            {
                StatusText = string.Empty;
            }

            _logger.Debug("[CompletionPopup] Filter '{Filter}' -> {Count} items", _filterText, _filteredItems.Count);
        }

        private bool FuzzyMatch(string text, string pattern)
        {
            // Simple fuzzy matching - checks if all pattern chars appear in order
            var textIndex = 0;
            foreach (var c in pattern)
            {
                textIndex = text.IndexOf(c, textIndex);
                if (textIndex < 0) return false;
                textIndex++;
            }
            return true;
        }

        private Point AdjustPositionForScreenBounds(Point position)
        {
            // Get the screen containing the position
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)position.X, (int)position.Y));

            var workArea = screen.WorkingArea;
            var popupWidth = Width;
            var popupHeight = MaxHeight;

            var x = position.X;
            var y = position.Y;

            // Adjust horizontal position
            if (x + popupWidth > workArea.Right)
            {
                x = workArea.Right - popupWidth;
            }
            if (x < workArea.Left)
            {
                x = workArea.Left;
            }

            // Adjust vertical position - flip above cursor if needed
            if (y + popupHeight > workArea.Bottom)
            {
                // Position above the cursor (subtract estimated line height)
                y = position.Y - popupHeight - 20;
            }
            if (y < workArea.Top)
            {
                y = workArea.Top;
            }

            return new Point(x, y);
        }

        private void AnimateFadeIn()
        {
            _isAnimating = true;

            var targetOpacity = SettingsService.Instance?.Settings?.PopupOpacity ?? 1.0;
            var animation = new DoubleAnimation
            {
                From = 0,
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            animation.Completed += (s, e) => _isAnimating = false;
            BeginAnimation(OpacityProperty, animation);
        }

        private void AnimateFadeOut(Action onComplete)
        {
            _isAnimating = true;

            var animation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            animation.Completed += (s, e) =>
            {
                _isAnimating = false;
                onComplete?.Invoke();
            };

            BeginAnimation(OpacityProperty, animation);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Event args for completion commit.
    /// </summary>
    public class CompletionCommitEventArgs : EventArgs
    {
        public CompletionCommitEventArgs(string insertText, CompletionItem item, string filterText)
        {
            InsertText = insertText;
            Item = item;
            FilterText = filterText;
        }

        /// <summary>
        /// Gets the text to insert.
        /// </summary>
        public string InsertText { get; }

        /// <summary>
        /// Gets the committed completion item.
        /// </summary>
        public CompletionItem Item { get; }

        /// <summary>
        /// Gets the filter text that was typed.
        /// </summary>
        public string FilterText { get; }
    }
}
