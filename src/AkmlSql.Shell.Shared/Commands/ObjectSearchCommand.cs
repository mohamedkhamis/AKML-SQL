#nullable enable
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Productivity.Navigation;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Object Search command (Ctrl+T): opens the <see cref="ObjectSearchWindow"/> popup
    /// for fuzzy searching database objects by name. When an object is selected,
    /// navigates to its definition.
    /// </summary>
    internal sealed class ObjectSearchCommand
    {
        private readonly AsyncPackage? _asyncPackage;
        private readonly Package? _syncPackage;

        private ObjectSearchCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _asyncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdObjectSearch);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        private ObjectSearchCommand(Package package, OleMenuCommandService commandService)
        {
            _syncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdObjectSearch);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static ObjectSearchCommand? Instance { get; private set; }

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new ObjectSearchCommand(package, commandService);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new ObjectSearchCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Get a session ID for the current context
                var sessionId = Guid.NewGuid().ToString("N");

                var searchWindow = new ObjectSearchWindow(sessionId);
                searchWindow.ObjectSelected += OnObjectSelected;
                searchWindow.Show();

                Log.Debug("ObjectSearchCommand: search window opened");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ObjectSearchCommand: failed to open search window");
            }
        }

        private void OnObjectSelected(object? sender, ObjectSelectedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var manager = EngineLifecycle.Manager;
                    if (manager?.Client == null || !manager.Client.IsConnected)
                        return;

                    var request = new GetObjectDefinitionRequest
                    {
                        SessionId = string.Empty,
                        ObjectName = e.ObjectName,
                        SchemaName = e.SchemaName,
                        PeekOnly = false
                    };

                    var response = await manager.Client
                        .SendRequestAsync<GetObjectDefinitionResponse, GetObjectDefinitionRequest>(
                            MessageTypes.GetObjectDefinition, request, timeoutMs: 10000);

                    if (response.Success && !string.IsNullOrEmpty(response.Definition))
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        OpenDefinitionInNewTab(response.Definition,
                            response.FullName ?? $"{e.SchemaName}.{e.ObjectName}",
                            response.ObjectType ?? e.ObjectType);
                    }
                    else
                    {
                        Log.Information("ObjectSearchCommand: definition not found for {Schema}.{Object}: {Error}",
                            e.SchemaName, e.ObjectName, response.Error);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ObjectSearchCommand: failed to navigate to {Schema}.{Object}",
                        e.SchemaName, e.ObjectName);
                }
            });
        }

        private static void OpenDefinitionInNewTab(string definition, string objectName, string objectType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = (EnvDTE80.DTE2)Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                var tempDir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "AkmlSql", "Definitions");
                System.IO.Directory.CreateDirectory(tempDir);

                var safeName = objectName.Replace(".", "_").Replace("[", "").Replace("]", "");
                var fileName = $"{safeName} ({objectType}).sql";
                var filePath = System.IO.Path.Combine(tempDir, fileName);

                System.IO.File.WriteAllText(filePath, definition);
                dte.ItemOperations.OpenFile(filePath, EnvDTE.Constants.vsViewKindCode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ObjectSearchCommand: failed to open definition tab");
            }
        }
    }
}
