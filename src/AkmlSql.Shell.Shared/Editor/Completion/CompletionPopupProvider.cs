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

                    // Handle Ctrl+Space via Win32 message interception.
                    // SSMS 22's SQL editor is a Win32 control hosted in WPF — WPF
                    // PreviewKeyDown events don't propagate to it.
                    // ComponentDispatcher.ThreadPreprocessMessage intercepts raw Win32
                    // keyboard messages BEFORE the host processes them, which works
                    // for all hosted controls.
                    System.Windows.Interop.ComponentDispatcher.ThreadPreprocessMessage += (ref System.Windows.Interop.MSG msg, ref bool handled) =>
                    {
                        // WM_KEYDOWN = 0x0100, VK_SPACE = 0x20
                        if (msg.message == 0x0100 && msg.wParam.ToInt32() == 0x20)
                        {
                            // Check if Ctrl is held
                            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
                            {
                                // Only handle if our text view has focus
                                if (textView.HasAggregateFocus)
                                {
                                    Log.Debug("ThreadPreprocessMessage: Ctrl+Space detected — triggering manual completion");
                                    controller.TriggerManualCompletion();
                                    handled = true;
                                }
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
            if (_nativeDisabled) return;
            _nativeDisabled = true;

            // Delay execution — the Query.IntelliSenseEnabled command requires
            // an active query window. Execute 2 seconds after first text view opens.
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(2000);
                try
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        try
                        {
                            var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                                typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                            if (dte == null) return;

                            // Toggle SSMS IntelliSense OFF via menu command
                            dte.ExecuteCommand("Query.IntelliSenseEnabled");
                            Log.Information("Executed Query.IntelliSenseEnabled to disable native IntelliSense");
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Query.IntelliSenseEnabled command failed");
                        }
                    });
                }
                catch { }
            });
        }
    }
}

