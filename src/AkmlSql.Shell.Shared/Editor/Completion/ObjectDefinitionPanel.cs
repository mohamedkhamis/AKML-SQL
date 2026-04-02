#nullable enable

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// SQL Prompt-style Object Definition panel shown alongside the completion popup.
    /// Displays two tabs: Summary (Label: Value detail pairs) and Script (CREATE DDL).
    /// Dark themed, code-only WPF (no XAML). ~300px wide.
    /// </summary>
    internal sealed class ObjectDefinitionPanel : Border
    {
        private readonly TabControl _tabControl;
        private readonly StackPanel _summaryContent;
        private readonly TextBlock _scriptContent;
        private readonly TextBlock _headerText;
        private readonly ScrollViewer _summaryScroll;
        private readonly ScrollViewer _scriptScroll;
        private bool _hasContent;

        private const double PanelWidth = 300;
        private const double MaxPanelHeight = 340;

        // SQL Prompt dark theme colors (matching AkmlCompletionPopup)
        private static readonly SolidColorBrush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x25, 0x28, 0x36)));
        private static readonly SolidColorBrush BorderBrush_ = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x4E)));
        private static readonly SolidColorBrush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)));
        private static readonly SolidColorBrush SecondaryBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));
        private static readonly SolidColorBrush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9C, 0xA0, 0xB0)));
        private static readonly SolidColorBrush HeaderBg = Freeze(new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)));
        private static readonly SolidColorBrush TabSelectedBg = Freeze(new SolidColorBrush(Color.FromRgb(0x09, 0x47, 0x71)));
        private static readonly SolidColorBrush TabNormalBg = Freeze(new SolidColorBrush(Color.FromRgb(0x2D, 0x30, 0x3D)));
        private static readonly SolidColorBrush RowSeparatorBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)));

        public ObjectDefinitionPanel()
        {
            // Header showing object type and name
            _headerText = new TextBlock
            {
                Foreground = TextBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 5, 8, 5),
                TextWrapping = TextWrapping.Wrap,
                Background = HeaderBg
            };

            // Summary tab content: list of Label: Value pairs
            _summaryContent = new StackPanel
            {
                Margin = new Thickness(0)
            };
            _summaryScroll = new ScrollViewer
            {
                Content = _summaryContent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = MaxPanelHeight - 70, // Leave room for header + tabs
                Background = BgBrush,
                Focusable = false
            };

            // Script tab content: DDL text in Consolas
            _scriptContent = new TextBlock
            {
                Foreground = TextBrush,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Padding = new Thickness(8, 4, 8, 4),
                TextWrapping = TextWrapping.Wrap,
                Background = BgBrush
            };
            _scriptScroll = new ScrollViewer
            {
                Content = _scriptContent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = MaxPanelHeight - 70,
                Background = BgBrush,
                Focusable = false
            };

            // Build tab control with Summary and Script tabs
            _tabControl = new TabControl
            {
                Background = BgBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Focusable = false
            };

            var summaryTab = CreateTab("Summary", _summaryScroll);
            var scriptTab = CreateTab("Script", _scriptScroll);

            _tabControl.Items.Add(summaryTab);
            _tabControl.Items.Add(scriptTab);
            _tabControl.SelectedIndex = 0;

            // Root layout
            var root = new StackPanel();
            root.Children.Add(_headerText);
            root.Children.Add(_tabControl);

            // Border styling
            Background = BgBrush;
            BorderBrush = BorderBrush_;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(3);
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 3,
                Opacity = 0.4,
                Color = Colors.Black
            };
            Child = root;
            Width = PanelWidth;
            Focusable = false;
            Visibility = Visibility.Collapsed;
        }

        /// <summary>True if the panel has content and is logically visible.</summary>
        public bool HasContent => _hasContent;

        /// <summary>
        /// Populate both tabs from a QuickInfoResponse.
        /// </summary>
        public void UpdateContent(
            string objectType,
            string header,
            AkmlSql.Core.Ipc.Messages.QuickInfoDetail[] details,
            string? description)
        {
            // Header
            var typeLabel = string.IsNullOrEmpty(objectType) ? "" : objectType + ": ";
            _headerText.Text = typeLabel + (header ?? string.Empty);

            // Summary tab: detail rows
            _summaryContent.Children.Clear();
            if (details != null)
            {
                foreach (var detail in details)
                {
                    var row = CreateDetailRow(detail.Label, detail.Value);
                    _summaryContent.Children.Add(row);
                }
            }

            if (details == null || details.Length == 0)
            {
                _summaryContent.Children.Add(new TextBlock
                {
                    Text = "No details available.",
                    Foreground = SecondaryBrush,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Padding = new Thickness(8, 6, 8, 6)
                });
            }

            // Script tab: DDL
            if (!string.IsNullOrWhiteSpace(description))
            {
                _scriptContent.Text = description;
            }
            else
            {
                _scriptContent.Text = "-- No script available";
            }

            // Switch to Summary tab by default
            _tabControl.SelectedIndex = 0;

            _hasContent = true;
            Visibility = Visibility.Visible;
        }

        /// <summary>Clear all content and hide the panel.</summary>
        public void Clear()
        {
            _headerText.Text = string.Empty;
            _summaryContent.Children.Clear();
            _scriptContent.Text = string.Empty;
            _hasContent = false;
            Visibility = Visibility.Collapsed;
        }

        /// <summary>Show the panel (if it has content).</summary>
        public void ShowPanel()
        {
            if (_hasContent)
                Visibility = Visibility.Visible;
        }

        /// <summary>Hide the panel without clearing content.</summary>
        public void HidePanel()
        {
            Visibility = Visibility.Collapsed;
        }

        private UIElement CreateDetailRow(string label, string value)
        {
            var labelBlock = new TextBlock
            {
                Text = label + ":",
                Foreground = LabelBrush,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                MinWidth = 80,
                Padding = new Thickness(8, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Top
            };

            var valueBlock = new TextBlock
            {
                Text = value ?? string.Empty,
                Foreground = TextBrush,
                FontSize = 11,
                Padding = new Thickness(0, 2, 8, 2),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(valueBlock, 1);

            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);

            return new Border
            {
                Child = grid,
                BorderBrush = RowSeparatorBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 1, 0, 1)
            };
        }

        private TabItem CreateTab(string header, UIElement content)
        {
            var headerBlock = new TextBlock
            {
                Text = header,
                Foreground = TextBrush,
                FontSize = 11,
                Padding = new Thickness(10, 3, 10, 3)
            };

            var tab = new TabItem
            {
                Header = headerBlock,
                Content = content,
                Background = TabNormalBg,
                BorderThickness = new Thickness(0),
                Focusable = false,
                Padding = new Thickness(0)
            };

            // Apply custom style for dark theme tab headers
            var style = new Style(typeof(TabItem));
            style.Setters.Add(new Setter(TabItem.BackgroundProperty, TabNormalBg));
            style.Setters.Add(new Setter(TabItem.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(TabItem.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(TabItem.FocusableProperty, false));

            var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(TabItem.BackgroundProperty, TabSelectedBg));
            style.Triggers.Add(selectedTrigger);

            tab.Style = style;

            return tab;
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
