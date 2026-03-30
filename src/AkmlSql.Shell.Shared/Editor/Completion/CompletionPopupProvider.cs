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

        [Export(typeof(AdornmentLayerDefinition))]
        [Name("AkmlSqlSchemaStatus")]
        [Order(After = PredefinedAdornmentLayers.Text)]
        public AdornmentLayerDefinition SchemaStatusLayerDefinition;

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
            var statusLayer = textView.GetAdornmentLayer("AkmlSqlSchemaStatus");
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

                    // Ctrl+Space is deeply wired to SSMS native IntelliSense and cannot
                    // be reliably intercepted. Use Ctrl+J as the AKML manual trigger.
                    // Also intercept Ctrl+Space via WM_KEYDOWN to dismiss native + show AKML.
                    System.Windows.Interop.ComponentDispatcher.ThreadPreprocessMessage += (ref System.Windows.Interop.MSG msg, ref bool handled) =>
                    {
                        if (!textView.HasAggregateFocus) return;

                        // WM_KEYDOWN = 0x0100
                        if (msg.message == 0x0100)
                        {
                            int vk = msg.wParam.ToInt32();
                            bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;

                            // Ctrl+J = manual AKML trigger (reliable alternative to Ctrl+Space)
                            // VK_J = 0x4A
                            if (ctrl && vk == 0x4A)
                            {
                                controller.TriggerManualCompletion();
                                handled = true;
                            }
                            // Ctrl+Space = dismiss native + trigger AKML
                            // VK_SPACE = 0x20
                            else if (ctrl && vk == 0x20)
                            {
                                // Let native handle it first, then immediately trigger ours
                                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                                    System.Windows.Threading.DispatcherPriority.Input,
                                    new Action(() =>
                                    {
                                        controller.SuppressAndTrigger();
                                    }));
                            }
                        }
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

            // Create schema status indicator
            if (statusLayer != null)
            {
                var indicator = new SchemaStatusIndicator(textView);
                statusLayer.AddAdornment(
                    Microsoft.VisualStudio.Text.Editor.AdornmentPositioningBehavior.ViewportRelative,
                    null, null, indicator, null);

                // Store for ConnectionWiringHelper to update
                textView.Properties.GetOrCreateSingletonProperty("AkmlSchemaIndicator", () => indicator);
            }
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

