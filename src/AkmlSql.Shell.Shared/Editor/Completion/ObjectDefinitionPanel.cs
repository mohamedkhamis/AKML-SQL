#nullable enable

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// SQL Prompt-style Object Definition panel shown alongside the completion popup.
    /// Displays two tabs: Summary (Label: Value detail pairs) and Script (CREATE DDL).
    /// Code-only WPF (no XAML), ~300px wide. Chrome flows through <see cref="ThemeRegistry"/>.
    /// </summary>
    internal sealed class ObjectDefinitionPanel : Border
    {
        // Theme-independent: a 12% white tint used as a row separator on top of the panel
        // background. Reads correctly on both Light and Dark surfaces because the alpha keeps
        // it as a soft tint rather than a hard rule.
        private static readonly SolidColorBrush RowSeparatorBrush = FrozenBrush(
            Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));

        private readonly TabControl _tabControl;
        private readonly StackPanel _summaryContent;
        private readonly TextBlock _scriptContent;
        private readonly TextBlock _headerText;
        private readonly ScrollViewer _summaryScroll;
        private readonly ScrollViewer _scriptScroll;
        private bool _hasContent;

        private const double PanelWidth     = 300;
        private const double MaxPanelHeight = 340;

        public ObjectDefinitionPanel()
        {
            // Attach the registry so SetResourceReference on this Border AND any descendant
            // resolves through ThemeRegistry.Resources.
            ThemeRegistry.Instance.AttachTo(this);

            // Header showing object type and name.
            _headerText = new TextBlock
            {
                FontSize     = Typography.Body,
                FontWeight   = FontWeights.SemiBold,
                Padding      = new Thickness(Spacing.Sm, 5, Spacing.Sm, 5),
                TextWrapping = TextWrapping.Wrap
            };
            _headerText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            _headerText.SetResourceReference(TextBlock.BackgroundProperty, ThemeTokens.SurfaceElevated);

            // Summary tab content: list of Label: Value pairs.
            _summaryContent = new StackPanel
            {
                Margin = new Thickness(0)
            };
            _summaryScroll = new ScrollViewer
            {
                Content                       = _summaryContent,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight                     = MaxPanelHeight - 70, // Leave room for header + tabs
                Focusable                     = false
            };
            _summaryScroll.SetResourceReference(ScrollViewer.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            // Script tab content: DDL text in a mono font.
            _scriptContent = new TextBlock
            {
                FontSize     = Typography.Small,
                FontFamily   = Typography.MonoFont,
                Padding      = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                TextWrapping = TextWrapping.Wrap
            };
            _scriptContent.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            _scriptContent.SetResourceReference(TextBlock.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            _scriptScroll = new ScrollViewer
            {
                Content                       = _scriptContent,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight                     = MaxPanelHeight - 70,
                Focusable                     = false
            };
            _scriptScroll.SetResourceReference(ScrollViewer.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            // Build tab control with Summary and Script tabs.
            _tabControl = new TabControl
            {
                BorderThickness = new Thickness(0),
                Padding         = new Thickness(0),
                Focusable       = false
            };
            _tabControl.SetResourceReference(TabControl.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            _tabControl.Items.Add(CreateTab("Summary", _summaryScroll));
            _tabControl.Items.Add(CreateTab("Script",  _scriptScroll));
            _tabControl.SelectedIndex = 0;

            // Root layout
            var root = new StackPanel();
            root.Children.Add(_headerText);
            root.Children.Add(_tabControl);

            // Border styling
            SetResourceReference(BackgroundProperty,  ThemeTokens.EditorPopupBackground);
            SetResourceReference(BorderBrushProperty, ThemeTokens.EditorPopupBorder);
            BorderThickness = new Thickness(1);
            CornerRadius    = new CornerRadius(3);
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius  = 10,
                ShadowDepth = 3,
                Opacity     = 0.4,
                Color       = Colors.Black
            };
            Child      = root;
            Width      = PanelWidth;
            Focusable  = false;
            Visibility = Visibility.Collapsed;
        }

        /// <summary>True if the panel has content and is logically visible.</summary>
        public bool HasContent => _hasContent;

        /// <summary>
        /// Populate the Summary tab from a QuickInfoResponse and reset the Script tab to a loading
        /// placeholder. The real CREATE script is supplied separately via <see cref="SetScript"/>
        /// (spec 030 T027 — the Script tab shows the object's DDL, not the MS_Description text; the
        /// description, when present, becomes a Summary row so it is not lost).
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

            // Summary tab: detail rows, then the description (if any) as a final row.
            _summaryContent.Children.Clear();
            int rowCount = 0;
            if (details != null)
            {
                foreach (var detail in details)
                {
                    _summaryContent.Children.Add(CreateDetailRow(detail.Label, detail.Value));
                    rowCount++;
                }
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                _summaryContent.Children.Add(CreateDetailRow("Description", description));
                rowCount++;
            }

            if (rowCount == 0)
            {
                var empty = new TextBlock
                {
                    Text      = "No details available.",
                    FontSize  = Typography.Small,
                    FontStyle = FontStyles.Italic,
                    Padding   = new Thickness(Spacing.Sm, 6, Spacing.Sm, 6)
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                _summaryContent.Children.Add(empty);
            }

            // Script tab: real DDL arrives asynchronously via SetScript — show a placeholder until then.
            _scriptContent.Text = "-- Loading definition…";

            // Switch to Summary tab by default
            _tabControl.SelectedIndex = 0;

            _hasContent = true;
            Visibility  = Visibility.Visible;
        }

        /// <summary>
        /// Spec 030 T027 (FR-017) — set the Script tab to the object's CREATE definition. When
        /// <paramref name="definition"/> is empty, shows a commented <paramref name="unavailableReason"/>
        /// (e.g. "No definition for this item type"). Called by the controller after the object
        /// definition is fetched from the engine.
        /// </summary>
        public void SetScript(string? definition, string? unavailableReason)
        {
            _scriptContent.Text = !string.IsNullOrWhiteSpace(definition)
                ? definition
                : "-- " + (string.IsNullOrWhiteSpace(unavailableReason) ? "No definition available" : unavailableReason);
        }

        /// <summary>Clear all content and hide the panel.</summary>
        public void Clear()
        {
            _headerText.Text = string.Empty;
            _summaryContent.Children.Clear();
            _scriptContent.Text = string.Empty;
            _hasContent = false;
            Visibility  = Visibility.Collapsed;
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
                Text              = label + ":",
                FontSize          = Typography.Small,
                FontWeight        = FontWeights.SemiBold,
                MinWidth          = 80,
                Padding           = new Thickness(Spacing.Sm, 2, Spacing.Xs, 2),
                VerticalAlignment = VerticalAlignment.Top
            };
            labelBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var valueBlock = new TextBlock
            {
                Text              = value ?? string.Empty,
                FontSize          = Typography.Small,
                Padding           = new Thickness(0, 2, Spacing.Sm, 2),
                TextWrapping      = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            valueBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(valueBlock, 1);

            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);

            return new Border
            {
                Child           = grid,
                BorderBrush     = RowSeparatorBrush,   // theme-independent 12% white tint
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding         = new Thickness(0, 1, 0, 1)
            };
        }

        private static TabItem CreateTab(string header, UIElement content)
        {
            var headerBlock = new TextBlock
            {
                Text     = header,
                FontSize = Typography.Small,
                Padding  = new Thickness(10, 3, 10, 3)
            };
            headerBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);

            var tab = new TabItem
            {
                Header          = headerBlock,
                Content         = content,
                BorderThickness = new Thickness(0),
                Focusable       = false,
                Padding         = new Thickness(0)
            };

            // Tab style with theme-aware Background via DynamicResourceExtension.
            var style = new Style(typeof(TabItem));
            style.Setters.Add(new Setter(TabItem.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.SurfaceElevated)));
            style.Setters.Add(new Setter(TabItem.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(TabItem.PaddingProperty,         new Thickness(0)));
            style.Setters.Add(new Setter(TabItem.FocusableProperty,       false));

            var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(TabItem.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.SurfaceSelectionStrong)));
            style.Triggers.Add(selectedTrigger);

            tab.Style = style;

            return tab;
        }

        private static SolidColorBrush FrozenBrush(Color color)
        {
            var b = new SolidColorBrush(color);
            b.Freeze();
            return b;
        }
    }
}
