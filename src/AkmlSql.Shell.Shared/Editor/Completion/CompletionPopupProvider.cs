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
    }
}
