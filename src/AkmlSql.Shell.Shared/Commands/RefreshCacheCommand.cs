using System;
using System.ComponentModel.Design;
using System.Linq;
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

            // Diagnostic: confirm the MenuCommand round-trips through the same command
            // service the menu/keyboard will consult later. If FindCommand returns null,
            // the AddCommand silently failed and clicks/keypresses won't reach Execute.
            // Logger isn't initialized yet at this point (LoggerFactory.Initialize runs
            // later in package init) — defer the FindCommand log too.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    try
                    {
                        var found = commandService.FindCommand(cmdId);
                        Log.Information(
                            "RefreshCacheCommand ctor: AddCommand({Guid}, {Id:X}) → FindCommand returned {Result}",
                            PackageGuids.AkmlSqlCmdSetString, CommandIds.CmdRefreshCache,
                            found == null ? "NULL (registration silently failed)" : $"Type={found.GetType().Name}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "RefreshCacheCommand ctor: FindCommand round-trip failed");
                    }
                }),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        public static RefreshCacheCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new RefreshCacheCommand(package, commandService);

            // The VSCT-declared Ctrl+Shift+D binding has not dispatched in the field on
            // SSMS 22 — the embedded .cto registers it but SSMS doesn't appear to route
            // the keypress to our handler. Force the binding via DTE.Commands at runtime.
            //
            // This call is deferred to ApplicationIdle so it runs AFTER the rest of the
            // package init (in particular, LoggerFactory.Initialize, which happens at
            // line 129 of AkmlSqlPackage.cs — much later than this command's Initialize
            // at line 67). Without the defer, all Log calls inside EnsureKeyBinding are
            // dropped because Serilog hasn't been configured yet.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                new Action(EnsureKeyBinding),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Programmatically (re-)applies the Global::Ctrl+Shift+D binding to the
        /// Refresh Schema Cache command via the DTE Commands API. Safe no-op on
        /// failure — the menu item still works as a fallback.
        /// </summary>
        private static void EnsureKeyBinding()
        {
            Log.Information("RefreshCacheCommand: EnsureKeyBinding starting (deferred from package init)");
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null)
                {
                    Log.Warning("RefreshCacheCommand: DTE service unavailable; cannot programmatically register Ctrl+Shift+D");
                    return;
                }

                EnvDTE.Command cmd;
                try
                {
                    cmd = dte.Commands.Item(
                        "{" + PackageGuids.AkmlSqlCmdSetString + "}",
                        CommandIds.CmdRefreshCache);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "RefreshCacheCommand: Commands.Item lookup failed for cmdRefreshCache");
                    return;
                }

                if (cmd == null)
                {
                    Log.Warning("RefreshCacheCommand: Refresh Schema Cache command not found via DTE");
                    return;
                }

                // Read existing bindings so we can log them (helpful for diagnosis when
                // SSMS rejects the assignment because another command holds the chord).
                var existing = cmd.Bindings as object[] ?? Array.Empty<object>();
                var existingDesc = string.Join(", ", existing.Select(b => b?.ToString() ?? "(null)"));

                // Force the binding. Single string overwrites all existing bindings on
                // this command — that's what we want, since the only "existing" one was
                // the broken VSCT-declared editor-scope binding.
                cmd.Bindings = "Global::Ctrl+Shift+D";

                Log.Information(
                    "RefreshCacheCommand: registered Global::Ctrl+Shift+D for '{Name}' (previous bindings: [{Existing}])",
                    cmd.Name, existingDesc);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RefreshCacheCommand: failed to register Ctrl+Shift+D programmatically");
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            Log.Information("RefreshCacheCommand.Execute ENTERED");
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

            // Show the bottom-right toast in Loading state immediately so the user
            // gets visible feedback their refresh was received. Without this, the
            // schema-progress poll runs every 1000 ms and can miss the brief
            // NotLoaded → PhaseA transition on fast schemas, leaving the toast
            // unchanged and looking like the click did nothing.
            TryBeginRefreshOnActiveView();

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
        /// Looks up the active editor's <see cref="Editor.SchemaProgress.SchemaProgressMargin"/>
        /// (created per-view by <c>SchemaProgressListener</c> and stored as a
        /// singleton property on the text view) and forces it into the Loading
        /// state. Best-effort — any failure is logged at Debug and swallowed.
        /// </summary>
        private static void TryBeginRefreshOnActiveView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) return;
                textManager.GetActiveView(1, null, out var vsView);
                if (vsView == null) return;

                var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
                var adapters = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
                var wpfView = adapters?.GetWpfTextView(vsView);
                if (wpfView == null) return;

                if (wpfView.Properties.TryGetProperty<Editor.SchemaProgress.SchemaProgressMargin>(
                        typeof(Editor.SchemaProgress.SchemaProgressMargin), out var margin) &&
                    margin != null)
                {
                    margin.BeginRefresh();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "RefreshCacheCommand: failed to trigger BeginRefresh on active view");
            }
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

                // IVsTextLines is a COM interface; ITextBuffer is purely managed. They are
                // NOT assignment-compatible — go through the editor adapter factory, which
                // is the only supported way to bridge from VS's COM text surface to the
                // managed editor buffer across all VS/SSMS hosts.
                var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
                var adapters = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
                var managedBuffer = adapters?.GetDocumentBuffer(vsBuffer);
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
