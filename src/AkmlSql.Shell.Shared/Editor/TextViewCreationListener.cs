using System;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// MEF listener that fires when a SQL editor is opened.
    /// Bootstraps assembly resolver, logger, engine, and connection detection.
    /// NOTE: No [Import] properties — MEF import satisfaction failures silently
    /// prevent instantiation. Services are obtained programmatically instead.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class TextViewCreationListener : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            // Phase 1: bootstrap (no IPC type references)
            ExtensionAssemblyResolver.Register();
            AkmlSql.Core.Logging.LoggerFactory.Initialize();
            Ipc.EngineLifecycle.LaunchAsync(); // fire-and-forget

            try
            {
                Log.Information("TextViewCreated for content type: {Type}",
                    textView.TextBuffer.ContentType.TypeName);

                // Session ID stored as plain string
                var sessionId = textView.TextBuffer.Properties.GetOrCreateSingletonProperty(
                    "AkmlSqlSessionId", () => Guid.NewGuid().ToString("N"));

                // Phase 2: IPC wiring (isolated behind NoInlining)
                WireToEngine(textView, sessionId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TextViewCreationListener failed");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WireToEngine(IWpfTextView textView, string sessionId)
        {
            try
            {
                // Get service provider from the VS global service
                var sp = Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider as IServiceProvider;
                // Pass the specific text view so connection detection resolves
                // to THIS view's document, not whatever is currently focused.
                ConnectionWiringHelper.DetectAndSendConnection(sp, sessionId, textView);
                ConnectionWiringHelper.SendFullDocument(sessionId, textView.TextBuffer);
                textView.TextBuffer.Changed += (s, e) =>
                    ConnectionWiringHelper.OnBufferChanged(sessionId, e);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Engine wiring failed (non-critical)");
            }
        }
    }
}
