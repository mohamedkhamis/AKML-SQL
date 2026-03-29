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

                    // Handle Ctrl+Space via raw WPF keyboard event (SSMS swallows the
                    // VS completion command when native IntelliSense is disabled).
                    // Use e.KeyboardDevice.Modifiers (from the event) instead of the static
                    // Keyboard.Modifiers which can miss modifier state in hosted scenarios.
                    textView.VisualElement.PreviewKeyDown += (s, e) =>
                    {
                        // Ctrl+Space may arrive as Key.Space with Ctrl modifier,
                        // or as Key.None/Key.System — check both paths
                        var actualKey = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
                        var mods = e.KeyboardDevice.Modifiers;

                        // Only log when Ctrl is held to avoid flooding on every keystroke
                        if ((mods & System.Windows.Input.ModifierKeys.Control) != 0)
                        {
                            Log.Debug("PreviewKeyDown: Key={Key} SystemKey={SystemKey} ActualKey={ActualKey} Modifiers={Modifiers}",
                                e.Key, e.SystemKey, actualKey, mods);
                        }

                        if (actualKey == System.Windows.Input.Key.Space &&
                            (mods & System.Windows.Input.ModifierKeys.Control) != 0)
                        {
                            Log.Debug("PreviewKeyDown: Ctrl+Space detected — triggering manual completion");
                            controller.TriggerManualCompletion();
                            e.Handled = true;
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

