using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("T-SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class TextViewCreationListener : IWpfTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService AdapterService { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            try
            {
                Log.Information("TextViewCreated for content type: {Type}",
                    textView.TextBuffer.ContentType.TypeName);

                var vsView = AdapterService.GetViewAdapter(textView);
                if (vsView == null)
                {
                    return;
                }

                // Attach command handler for keystroke interception
                var filter = new CompletionCommandHandler(textView, vsView);
                vsView.AddCommandFilter(filter, out var nextTarget);
                filter.NextTarget = nextTarget;

                // TODO (T039): Detect SSMS active connection changes via ServiceCache.ScriptFactory.
                // SSMS exposes the current connection through the IVsMonitorSelection service and
                // the ScriptFactory/QueryExecutionSettings COM objects. When a user changes the
                // connection dropdown or executes "USE [db]", we need to:
                //   1. Obtain the new connection string from ServiceCache.ScriptFactory
                //   2. Extract server/database/version info
                //   3. Send a ConnectionChanged message via EngineLifecycle.Manager.Client
                // This requires SSMS-specific COM interop that differs between SSMS 20 (IsolatedShell)
                // and SSMS 21/22 (VS-based). Implementation deferred to a future task.
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create text view listener");
            }
        }
    }
}
