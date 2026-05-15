#nullable enable
using System;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.PlatformUI;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 020 US3 (T052/T053/T054/T055/T060) — Format Styles editor modal window.
    /// Three-column layout matching SQL Prompt's documented Edit Formatting Styles editor
    /// (<c>doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md §8</c>):
    /// <list type="bullet">
    ///   <item>Left: style list with built-in vs Native badge, IsReadOnly lock icon.</item>
    ///   <item>Middle: settings tree built from the engine's <c>FormatSettingSchema</c>.</item>
    ///   <item>Right: controls + live preview placeholder (Tier 2b — controls panel and
    ///   preview wiring land in the follow-up commit).</item>
    /// </list>
    ///
    /// <para>
    /// Programmatic WPF only (no XAML) — matches the established pattern in
    /// <see cref="Ui.ProfileEditorDialog"/>. Chrome flows from <see cref="ThemeRegistry"/>
    /// via <c>SetResourceReference</c>; brushes are pre-frozen at palette-build time so no
    /// per-call allocation is needed. Owner is set from the DTE HWND so the dialog
    /// centres on the host's main window.
    /// </para>
    /// </summary>
    internal sealed class FormatStylesEditorWindow : DialogWindow
    {
        private readonly FormatStylesEditorViewModel _viewModel;
        private ListBox? _styleList;
        private TreeView? _settingsTree;
        private StackPanel? _settingControlsHost;
        private TextBlock? _settingControlsEmpty;
        private TextBox? _previewTextBox;
        private TextBlock? _statusText;

        public FormatStylesEditorWindow(FormatStylesEditorViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            Title = "AKML SQL — Format Styles Editor";
            Width = 1000;
            Height = 680;
            MinWidth = 800;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            HasHelpButton = false;

            // Ensure theme resources are merged so SetResourceReference resolves.
            ThemeRegistry.Instance.AttachTo(this);

            BuildUi();
            DataContext = _viewModel;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Loaded += OnLoaded;
        }

        // -----------------------------------------------------------------

        private void BuildUi()
        {
            var res = ThemeRegistry.Instance.Resources;
            var fg = (SolidColorBrush)res[ThemeTokens.TextPrimary];

            // Outer grid: header / content / footer
            var root = new Grid();
            root.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceCanvas);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Header ─────────────────────────────────────────────────────
            var header = new Border
            {
                Padding = new Thickness(Spacing.Lg, Spacing.Md, Spacing.Lg, Spacing.Md),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            header.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            header.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfacePanel);
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
            var headerTitle = new TextBlock
            {
                Text = "Edit Formatting Styles",
                FontFamily = Typography.UiFont,
                FontSize = Typography.H4,
                FontWeight = Typography.WeightSemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            headerTitle.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            headerStack.Children.Add(headerTitle);
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Content (three columns + 2 splitters) ──────────────────────
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });   // splitter
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });   // splitter
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

            // Left: style list
            content.Children.Add(BuildLeftPanel());

            // Splitter
            var leftSplitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ShowsPreview = false,
            };
            leftSplitter.SetResourceReference(Control.BackgroundProperty, ThemeTokens.BorderSplitter);
            Grid.SetColumn(leftSplitter, 1);
            content.Children.Add(leftSplitter);

            // Middle: settings tree
            content.Children.Add(BuildMiddlePanel());

            // Splitter
            var rightSplitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ShowsPreview = false,
            };
            rightSplitter.SetResourceReference(Control.BackgroundProperty, ThemeTokens.BorderSplitter);
            Grid.SetColumn(rightSplitter, 3);
            content.Children.Add(rightSplitter);

            // Right: controls (top) + preview (bottom)
            content.Children.Add(BuildRightPanel());

            Grid.SetRow(content, 1);
            root.Children.Add(content);

            // ── Footer (status + buttons) ──────────────────────────────────
            var footer = new Border
            {
                Padding = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
                BorderThickness = new Thickness(0, 1, 0, 0),
            };
            footer.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            footer.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfacePanel);

            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = string.Empty,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Spacing.Md, 0),
            };
            _statusText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            Grid.SetColumn(_statusText, 0);
            footerGrid.Children.Add(_statusText);

            var closeBtn = new Button
            {
                Content = "Close",
                Padding = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
                MinWidth = 80,
                IsCancel = true,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            closeBtn.Click += (_, _) => Close();
            Grid.SetColumn(closeBtn, 1);
            footerGrid.Children.Add(closeBtn);

            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        // -----------------------------------------------------------------
        // Left panel — style list
        // -----------------------------------------------------------------
        private FrameworkElement BuildLeftPanel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = "Styles",
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                FontWeight = Typography.WeightSemiBold,
                Margin = new Thickness(Spacing.Md, Spacing.Md, Spacing.Md, Spacing.Sm),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            Grid.SetRow(label, 0);
            panel.Children.Add(label);

            _styleList = new ListBox
            {
                Margin = new Thickness(Spacing.Sm, 0, Spacing.Sm, Spacing.Sm),
                BorderThickness = new Thickness(1),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                ItemTemplate = BuildStyleListItemTemplate(),
            };
            _styleList.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfaceInput);
            _styleList.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            _styleList.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
            _styleList.ItemsSource = _viewModel.Profiles;
            _styleList.SelectionChanged += (_, _) =>
            {
                if (_styleList.SelectedItem is StyleListItem item)
                {
                    _viewModel.SelectedProfileName = item.Name;
                }
            };
            Grid.SetRow(_styleList, 1);
            panel.Children.Add(_styleList);

            Grid.SetColumn(panel, 0);
            return panel;
        }

        private static DataTemplate BuildStyleListItemTemplate()
        {
            // Programmatic DataTemplate. Renders: [lock-glyph if read-only]  Name  [Built-in badge]
            const string xaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Grid Margin='4,3,4,3'>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width='Auto' />
      <ColumnDefinition Width='*' />
      <ColumnDefinition Width='Auto' />
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column='0' Text='&#x1F512; ' Margin='0,0,4,0'
               Visibility='{Binding IsReadOnly, Converter={StaticResource BoolToVisibilityConverter}}' />
    <TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center' />
    <TextBlock Grid.Column='2' Text='{Binding Kind}' Margin='6,0,0,0' Opacity='0.6'
               FontSize='10' VerticalAlignment='Center' />
  </Grid>
</DataTemplate>";
            try
            {
                // The {StaticResource BoolToVisibilityConverter} will fail without the converter
                // resource. Fall back to a code-built template if XAML parse fails.
                using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(xaml));
                return (DataTemplate)System.Windows.Markup.XamlReader.Load(stream);
            }
            catch
            {
                // Code-built fallback — simpler, no converter dependency.
                var template = new DataTemplate(typeof(StyleListItem));
                var stackFactory = new FrameworkElementFactory(typeof(StackPanel));
                stackFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
                stackFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 3, 4, 3));

                var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
                nameFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
                nameFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                stackFactory.AppendChild(nameFactory);

                var kindFactory = new FrameworkElementFactory(typeof(TextBlock));
                kindFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Kind"));
                kindFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
                kindFactory.SetValue(UIElement.OpacityProperty, 0.6);
                kindFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
                kindFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                stackFactory.AppendChild(kindFactory);

                template.VisualTree = stackFactory;
                return template;
            }
        }

        // -----------------------------------------------------------------
        // Middle panel — settings tree built from schema JSON
        // -----------------------------------------------------------------
        private FrameworkElement BuildMiddlePanel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = "Settings",
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                FontWeight = Typography.WeightSemiBold,
                Margin = new Thickness(Spacing.Md, Spacing.Md, Spacing.Md, Spacing.Sm),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            Grid.SetRow(label, 0);
            panel.Children.Add(label);

            _settingsTree = new TreeView
            {
                Margin = new Thickness(Spacing.Sm, 0, Spacing.Sm, Spacing.Sm),
                BorderThickness = new Thickness(1),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            _settingsTree.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfaceInput);
            _settingsTree.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            _settingsTree.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
            _settingsTree.SelectedItemChanged += (_, e) =>
            {
                if (e.NewValue is FormatSettingNode setting)
                {
                    _viewModel.SelectedSettingId = setting.Id;
                    UpdateRightTopForSetting(setting);
                }
            };
            Grid.SetRow(_settingsTree, 1);
            panel.Children.Add(_settingsTree);

            Grid.SetColumn(panel, 2);
            return panel;
        }

        // -----------------------------------------------------------------
        // Right panel — controls (top, placeholder) + preview (bottom, placeholder)
        // -----------------------------------------------------------------
        private FrameworkElement BuildRightPanel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) }); // splitter
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40, GridUnitType.Star) });

            // Top: dynamic controls area — built per-selection by RenderControlsForSetting
            var topBorder = new Border
            {
                Margin = new Thickness(Spacing.Sm, Spacing.Md, Spacing.Md, Spacing.Sm),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(Spacing.Md),
            };
            topBorder.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            topBorder.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceInput);

            var topScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            _settingControlsHost = new StackPanel
            {
                Orientation = Orientation.Vertical,
            };
            _settingControlsEmpty = new TextBlock
            {
                Text = "Select a setting from the tree to edit it.",
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            _settingControlsEmpty.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _settingControlsHost.Children.Add(_settingControlsEmpty);
            topScroll.Content = _settingControlsHost;
            topBorder.Child = topScroll;
            Grid.SetRow(topBorder, 0);
            panel.Children.Add(topBorder);

            // Splitter (horizontal)
            var hSplitter = new GridSplitter
            {
                Height = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                ResizeDirection = GridResizeDirection.Rows,
                ShowsPreview = false,
            };
            hSplitter.SetResourceReference(Control.BackgroundProperty, ThemeTokens.BorderSplitter);
            Grid.SetRow(hSplitter, 1);
            panel.Children.Add(hSplitter);

            // Bottom: live preview — read-only mono text bound to PreviewText
            var bottomBorder = new Border
            {
                Margin = new Thickness(Spacing.Sm, Spacing.Sm, Spacing.Md, Spacing.Md),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(0),
            };
            bottomBorder.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            bottomBorder.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            _previewTextBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = Typography.MonoFont,
                FontSize = Typography.Body,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(Spacing.Md),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = "// Live preview will appear after the schema loads and a profile is selected.",
            };
            _previewTextBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            bottomBorder.Child = _previewTextBox;
            Grid.SetRow(bottomBorder, 2);
            panel.Children.Add(bottomBorder);

            Grid.SetColumn(panel, 4);
            return panel;
        }

        // -----------------------------------------------------------------
        // Schema → TreeView
        // -----------------------------------------------------------------
        private void RebuildSettingsTreeFromSchema(string schemaJson)
        {
            if (_settingsTree == null) return;

            _settingsTree.Items.Clear();

            try
            {
                using var doc = JsonDocument.Parse(schemaJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("groups", out var groupsEl) || !root.TryGetProperty("settings", out var settingsEl))
                {
                    return;
                }

                // Index settings by group for one-pass tree construction
                var settingsByGroup = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<FormatSettingNode>>(StringComparer.Ordinal);
                foreach (var s in settingsEl.EnumerateArray())
                {
                    var groupId = s.TryGetProperty("groupId", out var g) ? g.GetString() ?? string.Empty : string.Empty;
                    if (!settingsByGroup.TryGetValue(groupId, out var list))
                    {
                        list = new System.Collections.Generic.List<FormatSettingNode>();
                        settingsByGroup[groupId] = list;
                    }
                    list.Add(new FormatSettingNode
                    {
                        Id = s.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty,
                        DisplayName = s.TryGetProperty("displayName", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                        Type = s.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "Other" : "Other",
                        Status = s.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "Implemented" : "Implemented",
                        SqlPromptKey = s.TryGetProperty("sqlPromptKey", out var spEl) && spEl.ValueKind != JsonValueKind.Null ? spEl.GetString() : null,
                        DefaultJson = s.TryGetProperty("default", out var defEl) ? defEl.GetRawText() : "null",
                    });
                }

                foreach (var g in groupsEl.EnumerateArray())
                {
                    var groupId = g.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                    var displayName = g.TryGetProperty("displayName", out var nameEl) ? nameEl.GetString() ?? groupId : groupId;

                    var groupNode = new TreeViewItem
                    {
                        Header = displayName,
                        IsExpanded = true,
                    };
                    groupNode.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);

                    if (settingsByGroup.TryGetValue(groupId, out var children))
                    {
                        foreach (var child in children)
                        {
                            var childNode = new TreeViewItem { Header = BuildSettingNodeHeader(child) };
                            childNode.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
                            childNode.Tag = child;
                            childNode.Selected += (_, e) =>
                            {
                                // TreeView's SelectedItem is the data; but our items hold UIElements
                                // as Header. Track via Tag so SelectedItemChanged sees the data.
                                if (childNode.Tag is FormatSettingNode node)
                                {
                                    _viewModel.SelectedSettingId = node.Id;
                                    UpdateRightTopForSetting(node);
                                }
                                e.Handled = true;
                            };
                            groupNode.Items.Add(childNode);
                        }
                    }
                    _settingsTree.Items.Add(groupNode);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormatStylesEditor: failed to parse schema JSON");
            }
        }

        private FrameworkElement BuildSettingNodeHeader(FormatSettingNode setting)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            var name = new TextBlock
            {
                Text = setting.DisplayName,
                VerticalAlignment = VerticalAlignment.Center,
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            stack.Children.Add(name);

            // FR-023: unsupported badge — small muted pill adjacent to the label
            if (string.Equals(setting.Status, "Unsupported", StringComparison.OrdinalIgnoreCase))
            {
                stack.Children.Add(BuildUnsupportedBadge());
            }

            return stack;
        }

        /// <summary>
        /// T060 — small pill rendered for settings flagged <c>Unsupported</c> (FR-023). Tooltip
        /// explains the FR-023 contract: value is preserved on round-trip, but AKML's formatter
        /// doesn't honour the setting yet.
        /// </summary>
        private static Border BuildUnsupportedBadge()
        {
            var badge = new Border
            {
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(6, 1, 6, 1),
                CornerRadius = new CornerRadius(8),
                ToolTip = "Not yet supported by AKML's formatter. The imported value is preserved " +
                          "and will round-trip on export.",
            };
            badge.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceHover);
            badge.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            badge.BorderThickness = new Thickness(1);

            var text = new TextBlock
            {
                Text = "Unsupported",
                FontFamily = Typography.UiFont,
                FontSize = 10,
                FontWeight = Typography.WeightSemiBold,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            badge.Child = text;
            return badge;
        }

        /// <summary>
        /// Tier 2b — renders type-appropriate controls for the selected setting in the
        /// right-top panel. Bool → CheckBox; Int → numeric TextBox; Enum (string) → TextBox
        /// (free-form, AllowedEnumValues populated as suggestions later). Settings with
        /// <c>Status == "Unsupported"</c> render disabled per FR-023; their imported value
        /// is still visible.
        /// </summary>
        private void UpdateRightTopForSetting(FormatSettingNode setting)
        {
            if (_settingControlsHost == null) return;

            _settingControlsHost.Children.Clear();

            // Header — setting name + Unsupported badge (if applicable)
            var headerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, Spacing.Sm),
            };
            var nameText = new TextBlock
            {
                Text = setting.DisplayName,
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                FontWeight = Typography.WeightSemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            nameText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            headerStack.Children.Add(nameText);
            if (string.Equals(setting.Status, "Unsupported", StringComparison.OrdinalIgnoreCase))
            {
                headerStack.Children.Add(BuildUnsupportedBadge());
            }
            _settingControlsHost.Children.Add(headerStack);

            // Metadata line — Type, ID, SQL Prompt key
            var metaText = new TextBlock
            {
                Text = $"Type: {setting.Type}    ID: {setting.Id}    " +
                       (setting.SqlPromptKey != null
                           ? $"SQL Prompt: {setting.SqlPromptKey}"
                           : "(AKML-only)"),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                Margin = new Thickness(0, 0, 0, Spacing.Md),
                TextWrapping = TextWrapping.Wrap,
            };
            metaText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _settingControlsHost.Children.Add(metaText);

            // Type-driven control
            var isDisabled = string.Equals(setting.Status, "Unsupported", StringComparison.OrdinalIgnoreCase);
            var currentValue = _viewModel.GetWorkingValue(setting.Id);
            var control = BuildControlForSetting(setting, currentValue, isDisabled);
            _settingControlsHost.Children.Add(control);
        }

        /// <summary>
        /// Returns the type-appropriate WPF control for one setting. The control is wired
        /// to <c>viewModel.SetWorkingValue</c> on change so the live preview refreshes
        /// (debounced 100 ms via <c>QueuePreviewAsync</c>).
        /// </summary>
        private FrameworkElement BuildControlForSetting(FormatSettingNode setting, object? currentValue, bool isDisabled)
        {
            switch (setting.Type)
            {
                case "Bool":
                {
                    var initial = currentValue is bool b ? b : ParseBool(setting.DefaultJson);
                    var checkBox = new CheckBox
                    {
                        Content = setting.DisplayName,
                        IsChecked = initial,
                        IsEnabled = !isDisabled,
                        FontFamily = Typography.UiFont,
                        FontSize = Typography.Body,
                    };
                    checkBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
                    if (!isDisabled)
                    {
                        checkBox.Checked += (_, _) => _viewModel.SetWorkingValue(setting.Id, true);
                        checkBox.Unchecked += (_, _) => _viewModel.SetWorkingValue(setting.Id, false);
                    }
                    return checkBox;
                }
                case "Int":
                {
                    var initial = currentValue?.ToString() ?? setting.DefaultJson.Trim('"');
                    var textBox = new TextBox
                    {
                        Text = initial,
                        Width = 100,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        IsEnabled = !isDisabled,
                        FontFamily = Typography.UiFont,
                        FontSize = Typography.Body,
                        Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                    };
                    textBox.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfaceInput);
                    textBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
                    textBox.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
                    if (!isDisabled)
                    {
                        textBox.TextChanged += (_, _) =>
                        {
                            if (int.TryParse(textBox.Text, out var n))
                            {
                                _viewModel.SetWorkingValue(setting.Id, n);
                            }
                        };
                    }
                    return textBox;
                }
                case "Enum":
                {
                    var initial = currentValue?.ToString() ?? setting.DefaultJson.Trim('"');
                    // No AllowedEnumValues at this schema level yet — use a free-text TextBox,
                    // but mark string fields visually with a non-numeric width so they look
                    // different from Int controls.
                    var textBox = new TextBox
                    {
                        Text = initial,
                        Width = 240,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        IsEnabled = !isDisabled,
                        FontFamily = Typography.UiFont,
                        FontSize = Typography.Body,
                        Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                    };
                    textBox.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfaceInput);
                    textBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
                    textBox.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
                    if (!isDisabled)
                    {
                        textBox.TextChanged += (_, _) =>
                        {
                            _viewModel.SetWorkingValue(setting.Id, textBox.Text);
                        };
                    }
                    return textBox;
                }
                default:
                {
                    var readonlyText = new TextBlock
                    {
                        Text = $"({setting.Type}) {currentValue ?? setting.DefaultJson}",
                        FontFamily = Typography.MonoFont,
                        FontSize = Typography.Body,
                    };
                    readonlyText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                    return readonlyText;
                }
            }
        }

        private static bool ParseBool(string defaultJson)
        {
            return defaultJson.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------------------------------------------------
        // Lifecycle / wiring
        // -----------------------------------------------------------------

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Set Owner to the DTE main window so the dialog centres correctly.
            // Failure here is non-fatal (e.g. running outside VS during a test).
            TrySetDteOwner();

            UpdateStatus("Loading…");
            await _viewModel.LoadAsync().ConfigureAwait(true);

            if (!string.IsNullOrEmpty(_viewModel.SchemaJson))
            {
                RebuildSettingsTreeFromSchema(_viewModel.SchemaJson!);
            }

            UpdateStatus(_viewModel.LastError ?? $"Loaded {_viewModel.Profiles.Count} style(s).");
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FormatStylesEditorViewModel.IsLoading) && _statusText != null)
            {
                UpdateStatus(_viewModel.IsLoading ? "Loading…" : (_viewModel.LastError ?? $"{_viewModel.Profiles.Count} style(s)."));
            }
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.PreviewText) && _previewTextBox != null)
            {
                // Marshal to UI thread — preview refresh fires from a background Task.
                if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _previewTextBox.Text = _viewModel.PreviewText;
                    }));
                }
                else
                {
                    _previewTextBox.Text = _viewModel.PreviewText;
                }
            }
        }

        private void UpdateStatus(string text)
        {
            if (_statusText != null) _statusText.Text = text;
        }

        private void TrySetDteOwner()
        {
            try
            {
                var dte = (EnvDTE.DTE?)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                var hwnd = (IntPtr)dte.MainWindow.HWnd;
                if (hwnd != IntPtr.Zero)
                {
                    new System.Windows.Interop.WindowInteropHelper(this).Owner = hwnd;
                }
            }
            catch
            {
                // Owner is best-effort; CenterOwner falls back to CenterScreen if Owner can't be set.
            }
        }

        // -----------------------------------------------------------------
        // Public launch helper — callers (Options Format page button,
        // ExternalTools menu, debug pad, tests) hit this to open the editor.
        // -----------------------------------------------------------------

        /// <summary>
        /// Opens a fresh editor on the current process. Returns when the user closes it.
        /// </summary>
        public static void Launch()
        {
            try
            {
                var vm = new FormatStylesEditorViewModel();
                var window = new FormatStylesEditorWindow(vm);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "FormatStylesEditor: launch failed");
                MessageBox.Show(
                    "Failed to open Format Styles Editor: " + ex.Message,
                    "AKML SQL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    /// <summary>Internal DTO bound to each settings-tree leaf.</summary>
    internal sealed class FormatSettingNode
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Type { get; set; } = "Other";
        public string Status { get; set; } = "Implemented";
        public string? SqlPromptKey { get; set; }
        public string DefaultJson { get; set; } = "null";
    }
}
