using System;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
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
                                });
                                if (conn != null)
                                {
                                    var c = EngineLifecycle.Manager?.Client;
                                    if (c != null && c.IsConnected)
                                        await SendConnectionChangedAsync(c, sessionId, conn);
                                    else
                                        Log.Debug("Engine not connected for deferred connection send");
                                    return;
                                }
                            }
                            catch { /* retry */ }
                        }
                        Log.Debug("No SSMS connection detected after retries for session {SessionId}", sessionId);
                    });
                    return;
                }

                var client = EngineLifecycle.Manager?.Client;
                if (client != null && client.IsConnected)
                {
                    Task.Run(() => SendConnectionChangedAsync(client, sessionId, connection));
                    return;
                }

                // Engine not ready yet — retry in background
                Task.Run(async () =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        await Task.Delay(500);
                        var c = EngineLifecycle.Manager?.Client;
                        if (c != null && c.IsConnected)
                        {
                            await SendConnectionChangedAsync(c, sessionId, connection);
                            return;
                        }
                    }
                    Log.Warning("Engine did not connect within 10s, schema loading skipped");
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to detect/send SSMS connection");
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
                Log.Information("Sent ConnectionChanged: {Server}.{Database} for session {Session}",
                    conn.Server, conn.Database, sessionId);

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
