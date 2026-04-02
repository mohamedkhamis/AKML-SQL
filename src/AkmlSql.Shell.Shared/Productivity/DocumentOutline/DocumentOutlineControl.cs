#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Ipc.Messages;
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

        public DocumentOutlineControl()
        {
            _viewModel = new DocumentOutlineViewModel();
            DataContext = _viewModel;

            _treeView = new TreeView
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                ItemsSource = _viewModel.RootNodes,
                ItemTemplate = CreateNodeTemplate()
            };

            _treeView.SelectedItemChanged += OnSelectedItemChanged;

            var grid = new System.Windows.Controls.Grid();

            // Header
            var header = new TextBlock
            {
                Text = "Document Outline",
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(Color.FromRgb(240, 242, 248))
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            System.Windows.Controls.Grid.SetRow(header, 0);
            System.Windows.Controls.Grid.SetRow(_treeView, 1);

            grid.Children.Add(header);
            grid.Children.Add(_treeView);

            Content = grid;
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
