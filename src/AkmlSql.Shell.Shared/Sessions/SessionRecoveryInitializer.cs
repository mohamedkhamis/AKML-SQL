#nullable enable
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Sessions
{
    /// <summary>
    /// Coordinates session recovery on startup and then starts the auto-save timer.
    /// Called after the engine process has connected successfully.
    /// <list type="number">
    ///   <item>Sends a <see cref="SessionRestoreRequest"/> to the engine.</item>
    ///   <item>If recoverable sessions exist and <c>RestoreOnStartup="prompt"</c>, shows <see cref="SessionRecoveryDialog"/>.</item>
    ///   <item>If <c>RestoreOnStartup="always"</c>, restores all tabs automatically.</item>
    ///   <item>Starts the <see cref="SessionAutoSave"/> periodic timer.</item>
    /// </list>
    /// </summary>
    internal static class SessionRecoveryInitializer
    {
        /// <summary>
        /// Entry point called from the VS package's InitializeAsync after the engine is connected.
        /// </summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            try
            {
                var settings = ConfigManager.Load();

                if (!settings.Tabs.SessionRecovery)
                {
                    Log.Information("SessionRecovery: disabled in settings, skipping");
                    return;
                }

                var manager = EngineLifecycle.Manager;
                if (manager?.Client?.IsConnected != true)
                {
                    Log.Warning("SessionRecovery: engine not connected, skipping recovery check");
                    // Still start auto-save so future sessions are captured
                    SessionAutoSave.Initialize(package);
                    return;
                }

                // Query the engine for recoverable sessions
                SessionRestoreResponse restoreResponse;
                try
                {
                    restoreResponse = await manager.Client
                        .SendRequestAsync<SessionRestoreResponse, SessionRestoreRequest>(
                            MessageTypes.SessionRestore,
                            new SessionRestoreRequest(),
                            timeoutMs: 10000);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "SessionRecovery: failed to query recoverable sessions");
                    SessionAutoSave.Initialize(package);
                    return;
                }

                if (restoreResponse.HasRecoverableSessions && restoreResponse.Sessions.Length > 0)
                {
                    Log.Information("SessionRecovery: found {Count} recoverable sessions",
                        restoreResponse.Sessions.Length);

                    var mode = settings.Tabs.RestoreOnStartup?.ToLowerInvariant() ?? "prompt";

                    switch (mode)
                    {
                        case "always":
                            await RestoreAllTabsAsync(package, restoreResponse.Sessions, manager);
                            break;

                        case "prompt":
                            await PromptAndRestoreAsync(package, restoreResponse.Sessions, manager);
                            break;

                        case "never":
                            Log.Information("SessionRecovery: RestoreOnStartup=never, cleaning up sessions");
                            await DeleteAllSessionsAsync(restoreResponse.Sessions, manager);
                            break;

                        default:
                            Log.Warning("SessionRecovery: unknown RestoreOnStartup value '{Mode}', treating as prompt", mode);
                            await PromptAndRestoreAsync(package, restoreResponse.Sessions, manager);
                            break;
                    }
                }
                else
                {
                    Log.Debug("SessionRecovery: no recoverable sessions found");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SessionRecovery: initialization failed");
            }
            finally
            {
                // Always start auto-save, even if recovery failed
                SessionAutoSave.Initialize(package);
            }
        }

        /// <summary>
        /// Shows the recovery dialog on the UI thread and restores selected tabs.
        /// </summary>
        private static async Task PromptAndRestoreAsync(
            AsyncPackage package,
            RecoverableSessionDto[] sessions,
            EngineProcessManager manager)
        {
            // Must show dialog on the UI thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            using var dialog = new SessionRecoveryDialog(sessions);
            var result = dialog.ShowDialog();

            if (result == DialogResult.OK && dialog.SelectedTabs.Count > 0)
            {
                Log.Information("SessionRecovery: user selected {Count} tabs to restore", dialog.SelectedTabs.Count);
                await RestoreTabsAsync(package, dialog.SelectedTabs);

                // Delete the restored session file
                if (!string.IsNullOrEmpty(dialog.SelectedSessionId))
                {
                    await DeleteSessionAsync(dialog.SelectedSessionId!, manager);
                }
            }
            else
            {
                Log.Information("SessionRecovery: user dismissed recovery dialog");
                // Delete all recoverable sessions since user declined
                await DeleteAllSessionsAsync(sessions, manager);
            }
        }

        /// <summary>
        /// Automatically restores all tabs from the newest recoverable session.
        /// </summary>
        private static async Task RestoreAllTabsAsync(
            AsyncPackage package,
            RecoverableSessionDto[] sessions,
            EngineProcessManager manager)
        {
            // Restore from the newest session (first in the list, sorted by engine)
            var newest = sessions[0];
            var tabs = new List<TabSnapshotDto>(newest.Tabs);

            Log.Information("SessionRecovery: auto-restoring {Count} tabs from session {SessionId}",
                tabs.Count, newest.SessionId);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            await RestoreTabsAsync(package, tabs);

            // Delete the restored session
            await DeleteSessionAsync(newest.SessionId, manager);

            // Delete remaining sessions
            for (int i = 1; i < sessions.Length; i++)
            {
                await DeleteSessionAsync(sessions[i].SessionId, manager);
            }
        }

        /// <summary>
        /// Opens new editor documents for each selected tab, restoring content and cursor position.
        /// Must be called on the UI thread.
        /// </summary>
        private static async Task RestoreTabsAsync(AsyncPackage package, List<TabSnapshotDto> tabs)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = (DTE?)Package.GetGlobalService(typeof(DTE));
            if (dte == null)
            {
                Log.Warning("SessionRecovery: DTE not available, cannot restore tabs");
                return;
            }

            foreach (var tab in tabs)
            {
                try
                {
                    // If the file exists on disk, open it directly
                    if (!string.IsNullOrEmpty(tab.FilePath) && System.IO.File.Exists(tab.FilePath))
                    {
                        dte.ItemOperations.OpenFile(tab.FilePath, EnvDTE.Constants.vsViewKindCode);
                    }
                    else
                    {
                        // Create a new unsaved document with the recovered content
                        dte.ItemOperations.NewFile("General\\SQL Query", tab.Title ?? "Recovered.sql");
                        var activeDoc = dte.ActiveDocument;
                        if (activeDoc != null && !string.IsNullOrEmpty(tab.Content))
                        {
                            var textDoc = activeDoc.Object("TextDocument") as TextDocument;
                            if (textDoc != null)
                            {
                                var editPoint = textDoc.StartPoint.CreateEditPoint();
                                editPoint.Insert(tab.Content);
                            }
                        }
                    }

                    // Restore cursor position
                    var restoredDoc = dte.ActiveDocument;
                    if (restoredDoc != null && (tab.CursorLine > 0 || tab.CursorColumn > 0))
                    {
                        var textDoc = restoredDoc.Object("TextDocument") as TextDocument;
                        if (textDoc?.Selection != null)
                        {
                            // DTE uses 1-based line/column
                            textDoc.Selection.MoveToLineAndOffset(
                                Math.Max(tab.CursorLine + 1, 1),
                                Math.Max(tab.CursorColumn + 1, 1));
                        }
                    }

                    Log.Debug("SessionRecovery: restored tab '{Title}'", tab.Title);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "SessionRecovery: failed to restore tab '{Title}'", tab.Title);
                }
            }
        }

        private static async Task DeleteSessionAsync(string sessionId, EngineProcessManager manager)
        {
            try
            {
                if (manager.Client?.IsConnected != true)
                {
                    return;
                }

                await manager.Client.SendRequestAsync<SessionDeleteResponse, SessionDeleteRequest>(
                    MessageTypes.SessionDelete,
                    new SessionDeleteRequest { SessionId = sessionId },
                    timeoutMs: 5000);

                Log.Debug("SessionRecovery: deleted session {SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SessionRecovery: failed to delete session {SessionId}", sessionId);
            }
        }

        private static async Task DeleteAllSessionsAsync(
            RecoverableSessionDto[] sessions, EngineProcessManager manager)
        {
            foreach (var session in sessions)
            {
                await DeleteSessionAsync(session.SessionId, manager);
            }
        }
    }
}
