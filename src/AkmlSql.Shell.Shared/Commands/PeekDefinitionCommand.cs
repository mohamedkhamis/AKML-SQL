#nullable enable
using System;
using System.ComponentModel.Design;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Productivity.Navigation;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Peek Definition command (Alt+F12): extracts the identifier at the cursor position,
    /// sends a <see cref="GetObjectDefinitionRequest"/> to the engine, and shows the result
    /// in an inline <see cref="PeekDefinitionControl"/> adornment below the current line.
    /// </summary>
    internal sealed class PeekDefinitionCommand
    {
        private readonly AsyncPackage? _asyncPackage;
        private readonly Package? _syncPackage;

        private PeekDefinitionCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _asyncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdPeekDefinition);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        private PeekDefinitionCommand(Package package, OleMenuCommandService commandService)
        {
            _syncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdPeekDefinition);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static PeekDefinitionCommand? Instance { get; private set; }

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new PeekDefinitionCommand(package, commandService);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new PeekDefinitionCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ExecuteAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "PeekDefinitionCommand: failed");
                }
            });
        }

        private async Task ExecuteAsync()
        {
            var manager = EngineLifecycle.Manager;
            if (manager?.Client == null || !manager.Client.IsConnected)
            {
                Log.Warning("PeekDefinitionCommand: engine not connected");
                return;
            }

            // Extract identifier at cursor from UI thread
            string objectName = string.Empty;
            string? schemaName = null;
            string sessionId = string.Empty;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var (name, schema, session) = GoToDefinitionCommand.ExtractIdentifierAtCursor();
                objectName = name;
                schemaName = schema;
                sessionId = session;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PeekDefinitionCommand: failed to extract identifier");
                return;
            }

            if (string.IsNullOrEmpty(objectName))
            {
                Log.Debug("PeekDefinitionCommand: no identifier at cursor");
                return;
            }

            // Switch to background for IPC
            GetObjectDefinitionResponse? response = null;
            try
            {
                var request = new GetObjectDefinitionRequest
                {
                    SessionId = sessionId,
                    ObjectName = objectName,
                    SchemaName = schemaName,
                    PeekOnly = true
                };

                response = await manager.Client.SendRequestAsync<GetObjectDefinitionResponse, GetObjectDefinitionRequest>(
                    MessageTypes.GetObjectDefinition, request, timeoutMs: 10000);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PeekDefinitionCommand: IPC request failed");
                return;
            }

            if (response == null || !response.Success || string.IsNullOrEmpty(response.Definition))
            {
                Log.Information("PeekDefinitionCommand: object not found - {Error}", response?.Error);
                await ShowStatusBarMessageAsync($"Definition not found: {response?.Error}");
                return;
            }

            // Show peek control on UI thread
            var definition = response.Definition;
            var fullName = response.FullName ?? objectName;
            var objectType = response.ObjectType ?? "Object";

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                ShowPeekControl(definition, fullName, objectType);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PeekDefinitionCommand: failed to show peek control");
            }
        }

        /// <summary>
        /// Shows the peek definition control as a WPF popup below the current editor line.
        /// In VS/SSMS, this uses a simple floating window approach since full ITextViewLine
        /// adornment requires extensive infrastructure.
        /// </summary>
        private void ShowPeekControl(string definition, string objectName, string objectType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var peekControl = new PeekDefinitionControl(definition, objectName, objectType);

                // Create a simple VS-hosted popup window
                var popup = new System.Windows.Window
                {
                    Content = peekControl,
                    WindowStyle = System.Windows.WindowStyle.None,
                    ResizeMode = System.Windows.ResizeMode.CanResizeWithGrip,
                    SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
                    Topmost = true,
                    ShowInTaskbar = false,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                    MaxHeight = 400,
                    MaxWidth = 900,
                    MinWidth = 500,
                    MinHeight = 200
                };

                peekControl.Dismissed += (_, __) =>
                {
                    popup.Close();
                };

                peekControl.OpenFullRequested += (_, __) =>
                {
                    popup.Close();
                    // Open in full tab instead
                    OpenDefinitionInNewTab(definition, objectName, objectType);
                };

                popup.Deactivated += (_, __) =>
                {
                    // Close when focus is lost
                    popup.Close();
                };

                popup.KeyDown += (_, args) =>
                {
                    if (args.Key == System.Windows.Input.Key.Escape)
                    {
                        popup.Close();
                    }
                };

                popup.Show();
                Log.Debug("PeekDefinitionCommand: showing peek for {Object} ({Type})", objectName, objectType);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PeekDefinitionCommand: failed to create peek window");
            }
        }

        private void OpenDefinitionInNewTab(string definition, string objectName, string objectType)
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
                Log.Error(ex, "PeekDefinitionCommand: failed to open full definition");
            }
        }

        private static async Task ShowStatusBarMessageAsync(string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var statusBar = (IVsStatusbar?)Package.GetGlobalService(typeof(SVsStatusbar));
                statusBar?.SetText(message);
            }
            catch
            {
                // Best-effort status bar update
            }
        }
    }
}
