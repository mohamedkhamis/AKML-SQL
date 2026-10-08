using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Text;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Separated from TextViewCreationListener to avoid IPC type references in MEF-scanned classes.
    /// Handles connection detection → Engine notification and document text synchronization.
    /// </summary>
    internal static class ConnectionWiringHelper
    {
        public static void DetectAndSendConnection(IServiceProvider serviceProvider, string sessionId,
            Microsoft.VisualStudio.Text.Editor.IWpfTextView textView = null)
        {
            try
            {
                // DTE.ActiveDocument may not be ready when TextViewCreated fires.
                // Retry with a short delay to let the document initialize.
                var connection = SsmsConnectionDetector.TryDetectConnection(serviceProvider, textView);
                if (connection == null)
                {
                    Log.Debug("DetectAndSendConnection: initial detect returned null for session={SessionId}, starting 10×500ms retry loop", sessionId);
                    // Retry after a delay on a background thread
                    Task.Run(async () =>
                    {
                        for (int attempt = 0; attempt < 10; attempt++)
                        {
                            await Task.Delay(500);
                            try
                            {
                                // Must access DTE on UI thread
                                SsmsConnectionDetector.ConnectionResult conn = null;
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    conn = SsmsConnectionDetector.TryDetectConnection(serviceProvider, textView);
                                    MaybeApplyStoredSqlCredential(conn, textView);
                                });
                                if (conn != null)
                                {
                                    if (!conn.IsEngineUsable)
                                    {
                                        // Auth can't be silently reused by the engine
                                        // (SQL auth / AAD Interactive / etc). ParseCaption
                                        // already logged the one-time warning — just stop.
                                        Log.Debug("DetectAndSendConnection: session={SessionId} detected on attempt {Attempt} but auth={Auth} is not engine-usable; skipping send",
                                            sessionId, attempt + 1, conn.AuthMode);
                                        return;
                                    }
                                    Log.Debug("DetectAndSendConnection: session={SessionId} detected on attempt {Attempt} → {Server}.{Db} auth={Auth}",
                                        sessionId, attempt + 1, conn.Server, conn.Database, conn.AuthMode);
                                    // The engine starts with SSMS; for a tab open at startup it can still
                                    // be connecting. Skipping the send then left that tab without a schema.
                                    var found = conn;
                                    if (!await WhenEngineReadyAsync(EngineReady,
                                            () => SendConnectionChangedAsync(EngineLifecycle.Manager!.Client, sessionId, found)))
                                        Log.Warning("DetectAndSendConnection: engine did not connect within 10s for session={SessionId}; schema loading skipped", sessionId);
                                    return;
                                }
                            }
                            catch (Exception retryEx)
                            {
                                Log.Debug(retryEx, "DetectAndSendConnection: retry attempt {Attempt} threw for session={SessionId}",
                                    attempt + 1, sessionId);
                            }
                        }
                        Log.Debug("DetectAndSendConnection: no SSMS connection detected after 10 retries for session {SessionId} (unsaved / not-yet-connected buffer is normal)", sessionId);
                    });
                    return;
                }

                MaybeApplyStoredSqlCredential(connection, textView);

                if (!connection.IsEngineUsable)
                {
                    // Unsupported auth — engine cannot connect without prompting or
                    // credentials we don't have. Skip sending ConnectionChanged so we
                    // don't trigger a Phase A attempt that would fail with a noisy
                    // login-failed error. ParseCaption already logged a one-shot warning.
                    Log.Debug("DetectAndSendConnection: session={SessionId} detected {Server}.{Db} but auth={Auth} is not engine-usable; skipping send",
                        sessionId, connection.Server, connection.Database, connection.AuthMode);
                    return;
                }

                Log.Debug("DetectAndSendConnection: session={SessionId} detected synchronously → {Server}.{Db} auth={Auth}",
                    sessionId, connection.Server, connection.Database, connection.AuthMode);

                if (!EngineReady())
                    Log.Debug("DetectAndSendConnection: engine not connected yet for session={SessionId}, polling up to 10s", sessionId);
                Task.Run(async () =>
                {
                    if (!await WhenEngineReadyAsync(EngineReady,
                            () => SendConnectionChangedAsync(EngineLifecycle.Manager!.Client, sessionId, connection)))
                        Log.Warning("DetectAndSendConnection: engine did not connect within 10s for session={SessionId}; schema loading skipped", sessionId);
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to detect/send SSMS connection");
            }
        }

        private static bool EngineReady() => EngineLifecycle.Manager?.Client is { IsConnected: true };

        /// <summary>
        /// Runs <paramref name="send"/> once the engine is connected, checking every
        /// <paramref name="delayMs"/> ms up to <paramref name="attempts"/> times; false when it never
        /// connected. A connection is often found before the engine started with SSMS is up.
        /// </summary>
        internal static async Task<bool> WhenEngineReadyAsync(Func<bool> engineReady, Func<Task> send,
            int attempts = 20, int delayMs = 500)
        {
            for (var i = 0; ; i++)
            {
                if (engineReady())
                {
                    await send().ConfigureAwait(false);
                    return true;
                }
                if (i >= attempts) return false;
                await Task.Delay(delayMs).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Spec 029. For a SQL-auth detection: write the per-buffer <see cref="SqlAuthState"/> marker,
        /// and if a credential is already stored, fill the connection string + mark engine-usable so the
        /// connection flows through the existing send path. When no credential is stored, leave the
        /// connection not-engine-usable (the existing skip path runs) and NeedsCredentials=true so the
        /// margin shows the click-to-enter affordance. No-op for non-SQL auth, or when disabled by config.
        /// </summary>
        private static void MaybeApplyStoredSqlCredential(
            SsmsConnectionDetector.ConnectionResult conn,
            Microsoft.VisualStudio.Text.Editor.IWpfTextView textView)
        {
            try
            {
                if (conn == null || conn.AuthMode != SsmsConnectionDetector.AuthMode.SqlPassword) return;

                var settings = ConfigManager.Load();
                if (!settings.IntelliSense.EnableSqlAuthCredentials) return; // opt-out → behave like Unsupported

                var pwd = ResolveSqlAuthPassword(conn.DataSource, conn.Server, conn.Login);
                bool has = !string.IsNullOrEmpty(pwd);

                if (textView != null)
                {
                    textView.TextBuffer.Properties["AkmlSqlAuthState"] = new SqlAuthState
                    {
                        Server = conn.Server,
                        DataSource = conn.DataSource,
                        Database = conn.Database,
                        Login = conn.Login,
                        NeedsCredentials = !has
                    };
                }

                if (has)
                {
                    conn.ConnectionString = SsmsConnectionDetector.BuildSqlAuthConnectionString(
                        conn.DataSource, conn.Database, conn.Login, pwd);
                    conn.IsEngineUsable = true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "MaybeApplyStoredSqlCredential failed");
            }
        }

        /// <summary>
        /// Spec 029 follow-up. Resolve the SQL-auth password for (server, login), in order:
        /// (1) <b>inherit</b> it from SSMS's active query-window connection (zero prompt — SSMS already
        /// holds it), persisting it to the DPAPI store for resilience; (2) the existing DPAPI store.
        /// Returns null when neither yields a password (the caller then shows the click-to-enter
        /// affordance). MUST run on the UI thread — the inherit step reads the SSMS ScriptFactory.
        /// </summary>
        /// <param name="server">The server connected to (the credential store's key).</param>
        /// <param name="shownServer">The server as SSMS shows it; credentials stored under it before
        /// custom connection names were resolved are still found.</param>
        private static string ResolveSqlAuthPassword(string server, string shownServer, string login)
        {
            // Tier 1: inherit the password SSMS already holds for the active window (no prompt).
            if (SsmsConnectionDetector.TryGetActiveSqlAuthPassword(server, login, out var inherited)
                && !string.IsNullOrEmpty(inherited))
            {
                try { SqlCredentialStore.Save(server, login, inherited); }
                catch (Exception ex) { Log.Debug(ex, "ResolveSqlAuthPassword: persisting inherited credential failed (non-fatal)"); }
                return inherited;
            }

            // Tier 2: a previously entered/inherited credential from the DPAPI store.
            if (SqlCredentialStore.TryGet(server, login, out var stored) && !string.IsNullOrEmpty(stored))
                return stored;
            if (!string.Equals(server, shownServer, StringComparison.OrdinalIgnoreCase)
                && SqlCredentialStore.TryGet(shownServer, login, out var legacy) && !string.IsNullOrEmpty(legacy))
                return legacy;

            return null; // Tier 3: caller prompts via the click-to-enter affordance.
        }

        /// <summary>
        /// Spec 029. Called by the margin while a buffer is in NeedsCredentials (and after a successful
        /// dialog save): if a credential is now stored for the buffer's (server, login), build the SQL
        /// connection string, send ConnectionChanged, clear NeedsCredentials, and return true. Reads the
        /// stored marker — no caption parse, no DTE walk (cheap enough for the 1s poll). Returns false
        /// when there is no marker or no stored credential.
        /// </summary>
        public static bool TryResolveStoredSqlCredential(
            string sessionId, Microsoft.VisualStudio.Text.Editor.IWpfTextView textView)
        {
            try
            {
                if (textView == null) return false;
                if (!textView.TextBuffer.Properties.TryGetProperty<SqlAuthState>("AkmlSqlAuthState", out var state)
                    || state == null)
                    return false;
                var pwd = ResolveSqlAuthPassword(state.DataSource, state.Server, state.Login);
                if (string.IsNullOrEmpty(pwd))
                    return false;

                // A credential exists, but if the engine isn't connected yet there is nothing to send.
                // Leave NeedsCredentials=true and return false so the affordance persists and the next
                // poll retries — otherwise we'd clear the state, send nothing, and strand the window
                // with neither schema nor the click-to-enter affordance.
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                    return false;

                var conn = new SsmsConnectionDetector.ConnectionResult
                {
                    Server = state.Server,
                    DataSource = state.DataSource,
                    Database = state.Database,
                    Login = state.Login,
                    ConnectionString = SsmsConnectionDetector.BuildSqlAuthConnectionString(
                        state.DataSource, state.Database, state.Login, pwd),
                    AuthMode = SsmsConnectionDetector.AuthMode.SqlPassword,
                    IsEngineUsable = true
                };
                state.NeedsCredentials = false;
                Task.Run(() => SendConnectionChangedAsync(client, sessionId, conn));
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TryResolveStoredSqlCredential failed for session={Session}", sessionId);
                return false;
            }
        }

        // The open editors by session, so an engine that restarted (and knows none) gets each one's
        // text and connection again. Weak: a closed view is not kept alive here.
        private static readonly ConcurrentDictionary<string, WeakReference<Microsoft.VisualStudio.Text.Editor.IWpfTextView>> OpenViews =
            new ConcurrentDictionary<string, WeakReference<Microsoft.VisualStudio.Text.Editor.IWpfTextView>>();

        /// <summary>Remembers <paramref name="textView"/> under its session until it closes.</summary>
        public static void Track(string sessionId, Microsoft.VisualStudio.Text.Editor.IWpfTextView textView)
        {
            OpenViews[sessionId] = new WeakReference<Microsoft.VisualStudio.Text.Editor.IWpfTextView>(textView);
            textView.Closed += (_, __) => OpenViews.TryRemove(sessionId, out var ___);
        }

        /// <summary>
        /// The engine ran again after a crash (or being killed): it starts with no sessions, so the
        /// open editors had no schema, completions or Format SQL actions until reopened. Send each
        /// one's text and connection again.
        /// </summary>
        public static void OnEngineRestarted()
        {
            _ = Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var sp = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider as IServiceProvider;
                var resent = 0;
                foreach (var pair in OpenViews.ToArray())
                {
                    if (!pair.Value.TryGetTarget(out var view) || view.IsClosed)
                    {
                        OpenViews.TryRemove(pair.Key, out _);
                        continue;
                    }
                    SendFullDocument(pair.Key, view.TextBuffer);
                    DetectAndSendConnection(sp, pair.Key, view);
                    resent++;
                }
                Log.Information("Engine restarted: sent {Count} open editor(s) again", resent);
            });
        }

        /// <summary>
        /// <see cref="SendFullDocument"/> once the engine is connected — the engine started with
        /// SSMS may still be starting when the first editor opens, and a skipped send left that
        /// editor's text unknown to it.
        /// </summary>
        public static void SendFullDocumentWhenReady(string sessionId, ITextBuffer buffer)
        {
            if (EngineReady())
            {
                SendFullDocument(sessionId, buffer);
                return;
            }
            Task.Run(async () =>
            {
                if (!await WhenEngineReadyAsync(EngineReady, () => { SendFullDocument(sessionId, buffer); return Task.CompletedTask; }))
                    Log.Warning("SendFullDocumentWhenReady: engine did not connect within 10s for session={SessionId}", sessionId);
            });
        }

        public static void SendFullDocument(string sessionId, ITextBuffer buffer)
        {
            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                    return;

                var change = new DocumentChange
                {
                    SessionId = sessionId,
                    ChangeType = 0,
                    FullText = buffer.CurrentSnapshot.GetText()
                };
                Task.Run(() => client.SendNotificationAsync(MessageTypes.DocumentChanged, change));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to send document text");
            }
        }

        public static void OnBufferChanged(string sessionId, TextContentChangedEventArgs e)
        {
            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                    return;

                var change = new DocumentChange
                {
                    SessionId = sessionId,
                    ChangeType = 0,
                    FullText = e.After.GetText()
                };
                Task.Run(() => client.SendNotificationAsync(MessageTypes.DocumentChanged, change));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to send document change");
            }
        }

        private static async Task SendConnectionChangedAsync(
            PipeRpcClient client, string sessionId, SsmsConnectionDetector.ConnectionResult conn)
        {
            try
            {
                // Show status bar loading indicator
                SetStatusBar($"AKML SQL: Loading schema for {conn.Database}...");

                var info = new ConnectionInfo
                {
                    SessionId = sessionId,
                    ConnectionString = conn.ConnectionString,
                    DatabaseName = conn.Database,
                    ServerVersion = 0,
                    EngineEdition = 0
                };

                await client.SendNotificationAsync(MessageTypes.ConnectionChanged, info);
                Log.Information("Sent ConnectionChanged: {Server}.{Database} auth={Auth} for session {Session}",
                    conn.Server, conn.Database, conn.AuthMode, sessionId);

                // Wait for schema to load (poll Engine), then update status bar
                await Task.Delay(2000); // Give Phase A time
                SetStatusBar($"AKML SQL: {conn.Database} ready");
                await Task.Delay(3000);
                SetStatusBar("");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to send ConnectionChanged");
                SetStatusBar("");
            }
        }

        private static void SetStatusBar(string text)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    try
                    {
                        var sp = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider;
                        var statusBar = sp?.GetService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar))
                            as Microsoft.VisualStudio.Shell.Interop.IVsStatusbar;
                        statusBar?.SetText(text);
                    }
                    catch { }
                });
            }
            catch { }
        }
    }
}
