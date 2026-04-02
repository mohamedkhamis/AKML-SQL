#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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
    /// Periodically captures snapshots of all open editor tabs and sends them to the engine
    /// for persistence. On clean shutdown, sends a final snapshot with <c>IsNormalShutdown=true</c>
    /// so the engine excludes it from crash recovery. Guarded by <see cref="TabSettings.SessionRecovery"/>.
    /// </summary>
    internal static class SessionAutoSave
    {
        private static Timer? _timer;
        private static string _sessionId = string.Empty;
        private static AsyncPackage? _package;
        private static volatile bool _initialized;

        /// <summary>
        /// Gets the stable session ID generated on startup, usable by the recovery initializer.
        /// </summary>
        public static string SessionId => _sessionId;

        /// <summary>
        /// Initializes the auto-save timer. Safe to call multiple times; only the first call takes effect.
        /// </summary>
        /// <param name="package">The VS/SSMS async package for DTE access.</param>
        public static void Initialize(AsyncPackage package)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _package = package;
            _sessionId = Guid.NewGuid().ToString();

            var settings = ConfigManager.Load();
            if (!settings.Tabs.SessionRecovery)
            {
                Log.Information("SessionAutoSave: session recovery is disabled in settings");
                return;
            }

            var intervalMs = Math.Max(settings.Tabs.AutoSaveInterval, 10) * 1000;
            _timer = new Timer(OnTimerTick, null, intervalMs, intervalMs);

            Log.Information("SessionAutoSave: initialized with SessionId={SessionId}, interval={Interval}s",
                _sessionId, settings.Tabs.AutoSaveInterval);
        }

        /// <summary>
        /// Sends a final session snapshot with <c>IsNormalShutdown=true</c> and disposes the timer.
        /// Called during clean VS/SSMS shutdown.
        /// </summary>
        public static void OnShutdown()
        {
            _timer?.Dispose();
            _timer = null;

            if (!_initialized || string.IsNullOrEmpty(_sessionId))
            {
                return;
            }

            try
            {
                // Send final snapshot synchronously (shutdown context)
                var tabs = CaptureTabsSync();
                var request = new SessionSaveRequest
                {
                    SessionId = _sessionId,
                    SsmsProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                    IsNormalShutdown = true,
                    Tabs = tabs
                };

                var manager = EngineLifecycle.Manager;
                if (manager?.Client?.IsConnected == true)
                {
                    // Fire-and-forget with a short timeout — we cannot block shutdown indefinitely
                    manager.Client
                        .SendRequestAsync<SessionSaveResponse, SessionSaveRequest>(
                            MessageTypes.SessionSave, request, timeoutMs: 3000)
                        .Wait(TimeSpan.FromSeconds(3));

                    Log.Information("SessionAutoSave: clean shutdown snapshot sent ({Count} tabs)", tabs.Length);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SessionAutoSave: failed to send shutdown snapshot");
            }
        }

        private static void OnTimerTick(object? state)
        {
            // Fire-and-forget; exceptions are caught inside
            _ = Task.Run(async () =>
            {
                try
                {
                    var manager = EngineLifecycle.Manager;
                    if (manager?.Client?.IsConnected != true)
                    {
                        return;
                    }

                    TabSnapshotDto[] tabs;
                    // Must access DTE on the UI thread
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    tabs = CaptureTabsSync();
                    // Switch back off the UI thread for IPC
                    await Task.Yield();

                    if (tabs.Length == 0)
                    {
                        return;
                    }

                    var request = new SessionSaveRequest
                    {
                        SessionId = _sessionId,
                        SsmsProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                        IsNormalShutdown = false,
                        Tabs = tabs
                    };

                    await manager.Client.SendRequestAsync<SessionSaveResponse, SessionSaveRequest>(
                        MessageTypes.SessionSave, request, timeoutMs: 10000);

                    Log.Debug("SessionAutoSave: periodic snapshot sent ({Count} tabs)", tabs.Length);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "SessionAutoSave: periodic save failed");
                }
            });
        }

        /// <summary>
        /// Captures all open documents via DTE. Must be called on the UI thread.
        /// Extracts server/database/auth from the document's connection properties if available,
        /// but never stores passwords.
        /// </summary>
        private static TabSnapshotDto[] CaptureTabsSync()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (_package == null)
                {
                    return [];
                }

                var dte = (DTE?)Package.GetGlobalService(typeof(DTE));
                if (dte?.Documents == null)
                {
                    return [];
                }

                var tabs = new List<TabSnapshotDto>();
                int index = 0;

                foreach (Document doc in dte.Documents)
                {
                    try
                    {
                        var textDoc = doc.Object("TextDocument") as TextDocument;
                        string? content = null;
                        int cursorLine = 0;
                        int cursorColumn = 0;

                        if (textDoc != null)
                        {
                            var startPoint = textDoc.StartPoint.CreateEditPoint();
                            content = startPoint.GetText(textDoc.EndPoint);

                            var sel = textDoc.Selection;
                            if (sel != null)
                            {
                                cursorLine = sel.ActivePoint.Line - 1; // Convert to 0-based
                                cursorColumn = sel.ActivePoint.DisplayColumn - 1;
                            }
                        }

                        // Extract connection info from the document window caption or properties
                        string? server = null;
                        string? database = null;
                        string? authType = null;
                        TryExtractConnectionInfo(doc, out server, out database, out authType);

                        tabs.Add(new TabSnapshotDto
                        {
                            TabIndex = index,
                            Title = doc.Name,
                            Content = content,
                            FilePath = doc.FullName,
                            Server = server,
                            Database = database,
                            AuthType = authType,
                            CursorLine = cursorLine,
                            CursorColumn = cursorColumn,
                            IsPinned = false // VS/SSMS DTE does not expose pin state directly
                        });

                        index++;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "SessionAutoSave: failed to capture tab {Name}", doc.Name);
                    }
                }

                return tabs.ToArray();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SessionAutoSave: failed to enumerate documents");
                return [];
            }
        }

        /// <summary>
        /// Attempts to extract connection information from the document's window caption.
        /// SSMS typically shows "ServerName.DatabaseName - filename" in the caption.
        /// Falls back to empty strings if extraction fails. Never captures passwords.
        /// </summary>
        private static void TryExtractConnectionInfo(
            Document doc, out string? server, out string? database, out string? authType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            server = null;
            database = null;
            authType = null;

            try
            {
                // Try reading from the document window caption
                // SSMS format is typically: "ServerName.DatabaseName - QueryFile.sql"
                var window = doc.ActiveWindow;
                if (window?.Caption != null)
                {
                    var caption = window.Caption;
                    var dashIndex = caption.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (dashIndex > 0)
                    {
                        var connectionPart = caption.Substring(0, dashIndex).Trim();
                        var dotIndex = connectionPart.IndexOf('.');
                        if (dotIndex > 0)
                        {
                            server = connectionPart.Substring(0, dotIndex).Trim();
                            database = connectionPart.Substring(dotIndex + 1).Trim();
                        }
                        else
                        {
                            server = connectionPart;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "SessionAutoSave: could not extract connection info from {Doc}", doc.Name);
            }
        }
    }
}
