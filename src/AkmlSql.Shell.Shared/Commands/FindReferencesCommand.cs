#nullable enable
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
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
    /// Find References command (Shift+F12): extracts the identifier at the cursor position,
    /// sends a <see cref="FindReferencesRequest"/> to the engine, and shows results in
    /// the <see cref="ReferencesToolWindow"/>.
    /// </summary>
    internal sealed class FindReferencesCommand
    {
        private readonly Package _package;

        private FindReferencesCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdFindReferences);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        private FindReferencesCommand(Package package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdFindReferences);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static FindReferencesCommand? Instance { get; private set; }

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new FindReferencesCommand(package, commandService);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new FindReferencesCommand(package, commandService);
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
                    Log.Error(ex, "FindReferencesCommand: failed");
                }
            });
        }

        private async Task ExecuteAsync()
        {
            var manager = EngineLifecycle.Manager;
            if (manager?.Client == null || !manager.Client.IsConnected)
            {
                Log.Warning("FindReferencesCommand: engine not connected");
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
                Log.Error(ex, "FindReferencesCommand: failed to extract identifier");
                return;
            }

            if (string.IsNullOrEmpty(objectName))
            {
                Log.Debug("FindReferencesCommand: no identifier at cursor");
                return;
            }

            // Switch to background for IPC
            FindReferencesResponse? response = null;
            try
            {
                var request = new FindReferencesRequest
                {
                    SessionId = sessionId,
                    ObjectName = objectName,
                    SchemaName = schemaName
                };

                response = await manager.Client
                    .SendRequestAsync<FindReferencesResponse, FindReferencesRequest>(
                        MessageTypes.FindReferences, request, timeoutMs: 15000);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "FindReferencesCommand: IPC request failed");
                return;
            }

            if (response == null || !response.Success)
            {
                await ShowStatusBarMessageAsync($"Find references failed: {response?.Error}");
                return;
            }

            var fullObjectName = schemaName != null ? $"{schemaName}.{objectName}" : objectName;
            var references = response.References ?? Array.Empty<ObjectReferenceDto>();

            Log.Information("FindReferencesCommand: found {Count} references to {Object}", references.Length, fullObjectName);

            // Show results in tool window on UI thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var window = _package.FindToolWindow(typeof(ReferencesToolWindow), 0, create: true);
                if (window?.Frame == null)
                {
                    Log.Warning("FindReferencesCommand: failed to create ReferencesToolWindow");
                    return;
                }

                var referencesWindow = (ReferencesToolWindow)window;
                referencesWindow.SetReferences(fullObjectName, references);

                var windowFrame = (IVsWindowFrame)window.Frame;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "FindReferencesCommand: failed to show references window");
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
