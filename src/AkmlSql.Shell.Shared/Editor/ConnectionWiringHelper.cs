using System;
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
                                    var c = EngineLifecycle.Manager?.Client;
                                    if (c != null && c.IsConnected)
                                        await SendConnectionChangedAsync(c, sessionId, conn);
                                    else
                                        Log.Debug("DetectAndSendConnection: engine not connected — deferred send skipped for session={SessionId}", sessionId);
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

                var client = EngineLifecycle.Manager?.Client;
                if (client != null && client.IsConnected)
                {
                    Task.Run(() => SendConnectionChangedAsync(client, sessionId, connection));
                    return;
                }

                // Engine not ready yet — retry in background
                Log.Debug("DetectAndSendConnection: engine not connected yet for session={SessionId}, polling up to 10s", sessionId);
                Task.Run(async () =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        await Task.Delay(500);
                        var c = EngineLifecycle.Manager?.Client;
                        if (c != null && c.IsConnected)
                        {
                            Log.Debug("DetectAndSendConnection: engine became ready after {Ms}ms for session={SessionId}", (i + 1) * 500, sessionId);
                            await SendConnectionChangedAsync(c, sessionId, connection);
                            return;
                        }
                    }
                    Log.Warning("DetectAndSendConnection: engine did not connect within 10s for session={SessionId}; schema loading skipped", sessionId);
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to detect/send SSMS connection");
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

                bool has = SqlCredentialStore.TryGet(conn.Server, conn.Login, out var pwd);

                if (textView != null)
                {
                    textView.TextBuffer.Properties["AkmlSqlAuthState"] = new SqlAuthState
                    {
                        Server = conn.Server,
                        Database = conn.Database,
                        Login = conn.Login,
                        NeedsCredentials = !has
                    };
                }

                if (has)
                {
                    conn.ConnectionString = SsmsConnectionDetector.BuildSqlAuthConnectionString(
                        conn.Server, conn.Database, conn.Login, pwd);
                    conn.IsEngineUsable = true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "MaybeApplyStoredSqlCredential failed");
            }
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
                if (!SqlCredentialStore.TryGet(state.Server, state.Login, out var pwd))
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
                    Database = state.Database,
                    Login = state.Login,
                    ConnectionString = SsmsConnectionDetector.BuildSqlAuthConnectionString(
                        state.Server, state.Database, state.Login, pwd),
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
