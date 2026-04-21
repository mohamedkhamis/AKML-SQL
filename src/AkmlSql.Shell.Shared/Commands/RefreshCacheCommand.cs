using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;
// Disambiguate: Microsoft.VisualStudio.Shell.Task (only defined in VS SDK 15.x/16.x
// used by SSMS 20 and VS 2019) vs System.Threading.Tasks.Task. Newer SDKs don't
// have the shell-side Task, but the shared project compiles against all six — so
// we alias here to keep one source tree that builds everywhere. Matches the
// pattern in Sessions/SessionAutoSave.cs.
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Manual schema cache refresh — bound to <c>Ctrl+Shift+D</c> on the editor scope.
    /// Dispatches a <see cref="MessageTypes.SchemaRefreshRequest"/> carrying the
    /// <em>active</em> editor's sessionId so the engine re-runs Phase A + Phase B
    /// for that session's database only (not every cached database).
    /// </summary>
    internal sealed class RefreshCacheCommand
    {
        private RefreshCacheCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (commandService == null) throw new ArgumentNullException(nameof(commandService));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdRefreshCache);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static RefreshCacheCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new RefreshCacheCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Resolve the active editor's sessionId *on the UI thread*, then hand
            // off to a background task for the IPC send. Looking up the sessionId
            // later (inside Task.Run) would race: by the time it ran, the user
            // may have switched documents.
            var sessionId = TryGetActiveSessionId();
            if (string.IsNullOrEmpty(sessionId))
            {
                Log.Information("RefreshCacheCommand: no active AKML-wired SQL editor — nothing to refresh");
                return;
            }

            Log.Information("RefreshCacheCommand: Ctrl+Shift+D invoked for session={SessionId}", sessionId);

            _ = Task.Run(async () =>
            {
                try
                {
                    var manager = EngineLifecycle.Manager;
                    var client = manager?.Client;
                    if (client == null || !client.IsConnected)
                    {
                        Log.Warning("RefreshCacheCommand: engine not connected — refresh request dropped for session={SessionId}", sessionId);
                        return;
                    }

                    var request = new RefreshRequest { SessionId = sessionId };
                    await client.SendNotificationAsync(MessageTypes.SchemaRefreshRequest, request);
                    Log.Information("RefreshCacheCommand: schema refresh request sent for session={SessionId}", sessionId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "RefreshCacheCommand: failed to send refresh request for session={SessionId}", sessionId);
                }
            });
        }

        /// <summary>
        /// Reads the <c>AkmlSqlSessionId</c> property from the currently focused
        /// SQL editor's text buffer. Returns <c>null</c> if there is no active
        /// view, the view's buffer has no sessionId yet (not wired by
        /// <c>TextViewCreationListener</c>), or any lookup throws.
        /// </summary>
        private static string TryGetActiveSessionId()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null)
                {
                    Log.Debug("RefreshCacheCommand: IVsTextManager not available");
                    return null;
                }

                textManager.GetActiveView(1, null, out var vsView);
                if (vsView == null)
                {
                    Log.Debug("RefreshCacheCommand: no active IVsTextView");
                    return null;
                }

                vsView.GetBuffer(out var vsBuffer);
                if (vsBuffer == null)
                {
                    Log.Debug("RefreshCacheCommand: active view has no IVsTextLines buffer");
                    return null;
                }

                // Direct cast works in VS 2022+; the adapter factory path covers
                // SSMS and older hosts where IVsTextLines isn't an ITextBuffer.
                var managedBuffer = vsBuffer as ITextBuffer;
                if (managedBuffer == null)
                {
                    var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
                    var adapters = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
                    managedBuffer = adapters?.GetDocumentBuffer(vsBuffer);
                }

                if (managedBuffer == null)
                {
                    Log.Debug("RefreshCacheCommand: could not resolve managed ITextBuffer for active view");
                    return null;
                }

                if (managedBuffer.Properties.TryGetProperty<string>("AkmlSqlSessionId", out var sessionId)
                    && !string.IsNullOrEmpty(sessionId))
                {
                    return sessionId;
                }

                Log.Debug("RefreshCacheCommand: active buffer has no AkmlSqlSessionId — editor not yet wired");
                return null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "RefreshCacheCommand: failed to resolve active session id");
                return null;
            }
        }
    }
}
