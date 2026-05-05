using System;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// MEF provider that creates the custom completion popup adornment for each SQL editor.
    /// No [Import] properties — they silently prevent MEF instantiation in SSMS 22.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [Order(Before = "default")]
    internal sealed class CompletionPopupProvider : IWpfTextViewCreationListener
    {
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("AkmlSqlCompletion")]
        [Order(After = PredefinedAdornmentLayers.Text, Before = PredefinedAdornmentLayers.Caret)]
        public AdornmentLayerDefinition CompletionLayerDefinition;

        /// <summary>
        /// Single WH_KEYBOARD hook for the UI thread. Shared across all text views because
        /// WH_KEYBOARD is a thread-level hook — one hook per thread is sufficient to intercept
        /// keystrokes for every editor hosted on that thread.
        /// </summary>
        private static KeyboardHook _keyboardHook;
        private static readonly object _hookLock = new object();
        private static int _activeViewCount;

        /// <summary>
        /// The controller for the text view that currently has focus. Updated when views
        /// gain/lose focus. The keyboard hook callback uses this to route Ctrl+Space
        /// to the correct controller.
        /// </summary>
        private static CompletionController _activeController;

        public void TextViewCreated(IWpfTextView textView)
        {
            // Bootstrap assembly resolver before touching any IPC types
            ExtensionAssemblyResolver.Register();

            try
            {
                // Disable SSMS native IntelliSense via DTE options (one-time)
                DisableNativeIntelliSense();

                WireCompletion(textView);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CompletionPopupProvider: failed to wire completion");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireCompletion(IWpfTextView textView)
        {
            // Get adornment layers
            var completionLayer = textView.GetAdornmentLayer("AkmlSqlCompletion");
            if (completionLayer == null) return;

            // Get session ID (created by TextViewCreationListener)
            if (!textView.TextBuffer.Properties.TryGetProperty("AkmlSqlSessionId", out string sessionId))
                return;

            // Create popup adornment
            var adornment = new CompletionPopupAdornment(textView, completionLayer);

            // Create and wire controller
            var controller = new CompletionController(textView, adornment, sessionId);

            // Get the VS text view to add command filter
            try
            {
                var componentModel = (Microsoft.VisualStudio.ComponentModelHost.IComponentModel)
                    Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                        typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel));
                var adapterService = componentModel?.GetService<Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService>();
                var vsView = adapterService?.GetViewAdapter(textView);
                if (vsView != null)
                {
                    vsView.AddCommandFilter(controller, out var nextTarget);
                    controller.NextTarget = nextTarget;

                    // Install the thread-level keyboard hook (once per UI thread) to intercept
                    // Ctrl+Space before SSMS's native IntelliSense handler can consume it.
                    // WH_KEYBOARD hooks sit earlier in the message chain than IOleCommandTarget,
                    // WPF PreviewKeyDown, or ComponentDispatcher.ThreadPreprocessMessage.
                    InstallKeyboardHook();

                    // Track which controller is active based on text view focus.
                    // When Ctrl+Space fires, the hook routes to _activeController.
                    textView.GotAggregateFocus += (s, e) => _activeController = controller;
                    textView.LostAggregateFocus += (s, e) =>
                    {
                        if (_activeController == controller)
                            _activeController = null;
                    };

                    // Set as active immediately if this view already has focus
                    if (textView.HasAggregateFocus)
                        _activeController = controller;

                    // Uninstall hook when the last text view closes
                    textView.Closed += (s, e) =>
                    {
                        if (_activeController == controller)
                            _activeController = null;
                        UninstallKeyboardHookIfLast();
                    };

                    Log.Debug("CompletionPopupProvider: controller wired for session {Session}", sessionId);
                }

                // Fully disable native IntelliSense by hooking broker session creation
                var broker = componentModel?.GetService<Microsoft.VisualStudio.Language.Intellisense.ICompletionBroker>();
                if (broker != null)
                {
                    // Dismiss any native sessions as soon as they appear
                    textView.Properties.GetOrCreateSingletonProperty("AkmlBroker", () =>
                    {
                        broker.DismissAllSessions(textView);
                        return broker;
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CompletionPopupProvider: failed to add command filter");
            }

            // Schema progress is rendered by SchemaProgressMargin (bottom-right toast).
            // The legacy SchemaStatusIndicator is no longer instantiated here — its
            // ShowLoading / ShowReady methods were never invoked anywhere, so it was
            // an invisible Border floating in the visual tree.
        }

        /// <summary>
        /// Installs the WH_KEYBOARD hook on the current UI thread if not already installed.
        /// Thread-safe: uses a lock to protect against concurrent TextViewCreated calls.
        /// </summary>
        private static void InstallKeyboardHook()
        {
            lock (_hookLock)
            {
                _activeViewCount++;

                if (_keyboardHook != null)
                    return; // Already installed

                _keyboardHook = new KeyboardHook();
                _keyboardHook.Install(
                    onCtrlSpace: () =>
                    {
                        var ctrl = _activeController;
                        ctrl?.SuppressAndTrigger();
                    },
                    onFormat: () =>
                    {
                        // Ctrl+K,Y or Ctrl+D → Format Document
                        // MUST use BeginInvoke — Invoke deadlocks inside hook callback
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                // Invoke format directly via DTE (works even if package not loaded)
                                var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                                    typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                                if (dte != null)
                                {
                                    dte.ExecuteCommand("AKML_SQL.FormatDocument");
                                    Log.Information("Format via DTE command");
                                }
                            }
                            catch
                            {
                                // Fallback: invoke format logic directly
                                try
                                {
                                    FormatDirectly();
                                }
                                catch (Exception ex2)
                                {
                                    Log.Debug(ex2, "Format direct fallback failed");
                                }
                            }
                        }));
                    },
                    onRefresh: () =>
                    {
                        // Ctrl+Shift+D → Refresh Schema Cache (SQL Prompt parity).
                        // Must use BeginInvoke — Invoke deadlocks inside the hook callback.
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                                    typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                                if (dte != null)
                                {
                                    dte.ExecuteCommand("Edit.RefreshSchemaCache");
                                    Log.Information("Refresh via DTE command (Ctrl+Shift+D)");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Debug(ex, "Refresh via DTE command failed");
                            }
                        }));
                    });
            }
        }

        /// <summary>
        /// Decrements the active view count and uninstalls the keyboard hook when the
        /// last SQL editor view on this thread closes.
        /// </summary>
        private static void UninstallKeyboardHookIfLast()
        {
            lock (_hookLock)
            {
                _activeViewCount--;

                if (_activeViewCount <= 0)
                {
                    _activeViewCount = 0;
                    _keyboardHook?.Dispose();
                    _keyboardHook = null;
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void FormatDirectly()
        {
            // Get active text view and format via Engine RPC
            var textManager = (Microsoft.VisualStudio.TextManager.Interop.IVsTextManager)
                Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                    typeof(Microsoft.VisualStudio.TextManager.Interop.SVsTextManager));
            if (textManager == null) { Log.Warning("Format: no text manager"); return; }

            textManager.GetActiveView(1, null, out var vsView);
            if (vsView == null) { Log.Warning("Format: no active view"); return; }

            vsView.GetBuffer(out var vsBuffer);
            if (vsBuffer == null) return;

            vsBuffer.GetLastLineIndex(out var lastLine, out var lastCol);
            vsBuffer.GetLineText(0, 0, lastLine, lastCol, out var text);
            if (string.IsNullOrEmpty(text)) return;

            Log.Information("Format: formatting {Len} chars via Engine", text.Length);

            // Send format request to Engine
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var client = Ipc.EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected) { Log.Warning("Format: engine not connected"); return; }

                    var response = await client.SendRequestAsync<
                        AkmlSql.Core.Ipc.Messages.FormatResponse,
                        AkmlSql.Core.Ipc.Messages.FormatRequest>(
                        AkmlSql.Core.Ipc.MessageTypes.FormatDocument,
                        new AkmlSql.Core.Ipc.Messages.FormatRequest { Text = text },
                        timeoutMs: 10000);

                    if (response != null && response.Success && !string.IsNullOrEmpty(response.FormattedText))
                    {
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                            // Use IVsTextBuffer.Replace via IntPtr for the new text
                            var newText = response.FormattedText;
                            var ptr = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(newText);
                            try
                            {
                                vsBuffer.ReplaceLines(0, 0, lastLine, lastCol, ptr, newText.Length, null);
                            }
                            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr); }
                            Log.Information("Format: replaced with {Len} chars", newText.Length);
                        });
                    }
                }
                catch (Exception ex) { Log.Error(ex, "Format: Engine RPC failed"); }
            });
        }

        private static bool _nativeDisabled;

        /// <summary>
        /// Disables SSMS native IntelliSense via DTE automation options.
        /// This is the only reliable way to prevent SSMS's internal IntelliSense
        /// from triggering — the IOleCommandTarget and ICompletionBroker approaches
        /// can't catch SSMS's text-change-triggered IntelliSense.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DisableNativeIntelliSense()
        {
            // Keep native IntelliSense ENABLED so Ctrl+Space commands route through
            // IOleCommandTarget where we intercept them. Our WPF Popup renders on
            // top, and the 20ms suppress timer dismisses native sessions.
        }
    }
}

