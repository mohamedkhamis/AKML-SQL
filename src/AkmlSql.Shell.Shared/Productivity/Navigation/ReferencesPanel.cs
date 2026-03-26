#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Navigation
{
    /// <summary>
    /// GUID for the Find References tool window.
    /// </summary>
    [Guid(ToolWindowGuid)]
    public class ReferencesToolWindow : ToolWindowPane
    {
        public const string ToolWindowGuid = "A1B2C3D4-8888-9999-AAAA-BBCCDDEE0011";

        private readonly ReferencesPanel _panel;

        public ReferencesToolWindow() : base(null)
        {
            Caption = "Find References";
            _panel = new ReferencesPanel();
            Content = _panel;
        }

        /// <summary>
        /// Updates the references displayed in the panel.
        /// </summary>
        public void SetReferences(string objectName, ObjectReferenceDto[] references)
        {
            _panel.SetReferences(objectName, references);
        }
    }

    /// <summary>
    /// WPF UserControl that displays a list of references to a database object.
    /// Shows as a VS tool window pane. Each row shows the referencing object's
    /// schema.name, type, and optional line number. Click navigates to definition.
    /// </summary>
    internal sealed class ReferencesPanel : UserControl
    {
        private readonly ListView _listView;
        private readonly TextBlock _headerBlock;
        private readonly ObservableCollection<ReferenceViewModel> _references = new();

        /// <summary>
        /// Raised when a user double-clicks a reference to navigate to its definition.
        /// </summary>
        public event EventHandler<ReferenceNavigateEventArgs>? NavigateRequested;

        public ReferencesPanel()
        {
            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header
            _headerBlock = new TextBlock
            {
                Text = "No references loaded",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(8, 6, 8, 6)
            };
            System.Windows.Controls.Grid.SetRow(_headerBlock, 0);
            grid.Children.Add(_headerBlock);

            // ListView with columns
            _listView = new ListView
            {
                ItemsSource = _references,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12
            };

            var gridView = new GridView();

            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Type",
                Width = 100,
                DisplayMemberBinding = new System.Windows.Data.Binding("ObjectType")
            });

            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Object",
                Width = 250,
                DisplayMemberBinding = new System.Windows.Data.Binding("FullName")
            });

            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Line",
                Width = 60,
                DisplayMemberBinding = new System.Windows.Data.Binding("LineDisplay")
            });

            _listView.View = gridView;
            _listView.MouseDoubleClick += OnListViewDoubleClick;
            _listView.KeyDown += OnListViewKeyDown;

            System.Windows.Controls.Grid.SetRow(_listView, 1);
            grid.Children.Add(_listView);

            Content = grid;
        }

        /// <summary>
        /// Populates the references panel with a new set of references.
        /// </summary>
        public void SetReferences(string objectName, ObjectReferenceDto[] references)
        {
            _references.Clear();

            foreach (var reference in references)
            {
                _references.Add(new ReferenceViewModel(reference));
            }

            _headerBlock.Text = $"References to '{objectName}': {references.Length} found";
        }

        private void OnListViewDoubleClick(object sender, MouseButtonEventArgs e)
        {
            NavigateToSelected();
        }

        private void OnListViewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateToSelected();
                e.Handled = true;
            }
        }

        private void NavigateToSelected()
        {
            if (_listView.SelectedItem is ReferenceViewModel vm)
            {
                NavigateRequested?.Invoke(this, new ReferenceNavigateEventArgs(
                    vm.SchemaName, vm.ObjectName, vm.ReferenceLine));

                // Navigate to the referencing object's definition
                NavigateToDefinition(vm.SchemaName, vm.ObjectName);
            }
        }

        private static void NavigateToDefinition(string schemaName, string objectName)
        {
            try
            {
                // Trigger Go to Definition for the referencing object
                var manager = Ipc.EngineLifecycle.Manager;
                if (manager?.Client == null || !manager.Client.IsConnected)
                    return;

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var request = new GetObjectDefinitionRequest
                        {
                            SessionId = string.Empty,
                            ObjectName = objectName,
                            SchemaName = schemaName,
                            PeekOnly = false
                        };

                        var response = await manager.Client
                            .SendRequestAsync<GetObjectDefinitionResponse, GetObjectDefinitionRequest>(
                                Core.Ipc.MessageTypes.GetObjectDefinition, request, timeoutMs: 10000);

                        if (response.Success && !string.IsNullOrEmpty(response.Definition))
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                            var dte = (EnvDTE80.DTE2)Package.GetGlobalService(typeof(EnvDTE.DTE));
                            if (dte == null) return;

                            var tempDir = System.IO.Path.Combine(
                                System.IO.Path.GetTempPath(), "AkmlSql", "Definitions");
                            System.IO.Directory.CreateDirectory(tempDir);

                            var safeName = $"{schemaName}_{objectName}".Replace(".", "_");
                            var filePath = System.IO.Path.Combine(tempDir, $"{safeName}.sql");
                            System.IO.File.WriteAllText(filePath, response.Definition);
                            dte.ItemOperations.OpenFile(filePath, EnvDTE.Constants.vsViewKindCode);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "ReferencesPanel: failed to navigate to {Schema}.{Object}", schemaName, objectName);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ReferencesPanel: navigate failed");
            }
        }
    }

    /// <summary>
    /// View model for a single reference in the <see cref="ReferencesPanel"/>.
    /// </summary>
    internal sealed class ReferenceViewModel(ObjectReferenceDto dto)
    {
        public string SchemaName { get; } = dto.SchemaName;
        public string ObjectName { get; } = dto.ObjectName;
        public string ObjectType { get; } = dto.ObjectType;
        public int? ReferenceLine { get; } = dto.ReferenceLine;
        public string FullName => $"{SchemaName}.{ObjectName}";
        public string LineDisplay => ReferenceLine.HasValue ? ReferenceLine.Value.ToString() : "-";
    }

    /// <summary>
    /// Event args for navigating to a referenced object.
    /// </summary>
    internal sealed class ReferenceNavigateEventArgs(string schemaName, string objectName, int? line) : EventArgs
    {
        public string SchemaName { get; } = schemaName;
        public string ObjectName { get; } = objectName;
        public int? Line { get; } = line;
    }
}
