#nullable enable
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ui;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.DocumentOutline
{
    /// <summary>
    /// Programmatic WPF control hosting a TreeView for the Document Outline.
    /// Node templates display an icon (by NodeType) and name.
    /// Click handler scrolls the editor to the selected node's StartLine.
    /// </summary>
    internal sealed class DocumentOutlineControl : UserControl
    {
        private readonly TreeView _treeView;
        private readonly DocumentOutlineViewModel _viewModel;
        private readonly TextBlock _emptyState;

        public DocumentOutlineControl()
        {
            _viewModel = new DocumentOutlineViewModel();
            DataContext = _viewModel;

            _treeView = new TreeView
            {
                BorderThickness = new Thickness(0),
                ItemsSource = _viewModel.RootNodes,
                ItemTemplate = CreateNodeTemplate()
            };
            // Transparent background so the panel's color shows through (theme-token chrome below).
            _treeView.Background = System.Windows.Media.Brushes.Transparent;
            _treeView.SelectedItemChanged += OnSelectedItemChanged;

            // Empty-state message — shown when outline succeeds but finds no SQL structure.
            _emptyState = new TextBlock
            {
                Text            = "No SQL structure found — add CTEs, stored procedures, or functions to see them listed here",
                TextWrapping    = TextWrapping.Wrap,
                Padding         = new Thickness(Spacing.Md, Spacing.Lg, Spacing.Md, 0),
                FontFamily      = Typography.UiFont,
                FontSize        = Typography.Small,
                Visibility      = Visibility.Collapsed
            };
            _emptyState.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPlaceholder);

            // Subscribe to collection changes to toggle the empty state message.
            _viewModel.RootNodes.CollectionChanged += OnRootNodesChanged;

            var grid = new System.Windows.Controls.Grid();

            // Header row with title + Refresh button
            var headerPanel = new DockPanel
            {
                LastChildFill = false
            };
            headerPanel.SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceElevated);

            var headerText = new TextBlock
            {
                Text       = "Document Outline",
                FontFamily = Typography.UiFont,
                FontWeight = Typography.WeightSemiBold,
                Padding    = new Thickness(Spacing.Sm, 6, Spacing.Sm, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            DockPanel.SetDock(headerText, Dock.Left);

            var refreshButton = new Button
            {
                Content    = "\u21bb Refresh",
                Padding    = new Thickness(6, 2, 6, 2),
                Margin     = new Thickness(0, 3, Spacing.Xs, 3),
                FontSize   = Typography.Small,
                Cursor     = Cursors.Hand,
                BorderThickness = new Thickness(1),
                ToolTip    = "Rebuild the outline from the current document",
                FocusVisualStyle = FocusVisualStyles.HighStakes // FR-018 / O9 (high-stakes nav action)
            };
            refreshButton.SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceCanvas);
            refreshButton.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);
            refreshButton.SetResourceReference(BorderBrushProperty, ThemeTokens.BorderDefault);
            refreshButton.Click += (_, _) => _viewModel.RequestOutlineUpdate();
            DockPanel.SetDock(refreshButton, Dock.Right);

            headerPanel.Children.Add(headerText);
            headerPanel.Children.Add(refreshButton);

            // Content area: tree + empty state stack
            var contentStack = new System.Windows.Controls.Grid();
            contentStack.Children.Add(_treeView);
            contentStack.Children.Add(_emptyState);

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            System.Windows.Controls.Grid.SetRow(headerPanel, 0);
            System.Windows.Controls.Grid.SetRow(contentStack, 1);

            grid.Children.Add(headerPanel);
            grid.Children.Add(contentStack);

            Content = grid;
        }

        private void OnRootNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            var isEmpty = _viewModel.RootNodes.Count == 0;
            _emptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            _treeView.Visibility   = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Gets the ViewModel for external binding (e.g., from the tool window).
        /// </summary>
        public DocumentOutlineViewModel ViewModel => _viewModel;

        private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is OutlineNodeDto node)
            {
                _viewModel.SelectedNode = node;
                NavigateToLine(node.StartLine);
            }
        }

        /// <summary>
        /// Scrolls the active text editor to the given 0-based line number.
        /// </summary>
        private static void NavigateToLine(int line)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) return;

                textManager.GetActiveView(1, null, out var textView);
                if (textView == null) return;

                // Move caret and center the view on the target line
                textView.SetCaretPos(line, 0);
                textView.CenterLines(line, 1);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DocumentOutlineControl: failed to navigate to line {Line}", line);
            }
        }

        /// <summary>
        /// Creates a hierarchical DataTemplate for OutlineNodeDto with icon + name.
        /// </summary>
        private static HierarchicalDataTemplate CreateNodeTemplate()
        {
            var template = new HierarchicalDataTemplate
            {
                ItemsSource = new Binding("Children")
            };

            var factory = new FrameworkElementFactory(typeof(StackPanel));
            factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            factory.SetValue(MarginProperty, new Thickness(2, 1, 2, 1));
            factory.SetValue(CursorProperty, Cursors.Hand);

            // Icon (TextBlock with emoji-like glyph based on NodeType)
            var iconFactory = new FrameworkElementFactory(typeof(TextBlock));
            iconFactory.SetBinding(TextBlock.TextProperty, new Binding("NodeType")
            {
                Converter = new NodeTypeToIconConverter()
            });
            iconFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
            iconFactory.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
            iconFactory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.AppendChild(iconFactory);

            // Name
            var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
            nameFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            nameFactory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.AppendChild(nameFactory);

            template.VisualTree = factory;
            return template;
        }
    }

    /// <summary>
    /// Converts a NodeType string to a single-character icon glyph for the outline tree.
    /// </summary>
    internal sealed class NodeTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (value as string) switch
            {
                "Procedure" => "P",
                "Function" => "F",
                "View" => "V",
                "Trigger" => "T",
                "CTE" => "C",
                "TempTable" => "#",
                "Block" => "B",
                "Region" => "R",
                "Statement" => "S",
                _ => "?"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
