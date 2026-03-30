#nullable enable
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Toolbar
{
    /// <summary>
    /// MEF export that creates an <see cref="EditorToolbar"/> margin at the top of each SQL editor.
    /// Uses the IWpfTextViewMarginProvider pattern (same as VS StickyScroll) to inject a WPF
    /// toolbar above the text view. No [Import] properties — they silently prevent MEF
    /// instantiation in SSMS 22.
    /// </summary>
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name("AkmlSqlEditorToolbar")]
    [Order(Before = PredefinedMarginNames.Top)]
    [MarginContainer(PredefinedMarginNames.Top)]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class EditorToolbarProvider : IWpfTextViewMarginProvider
    {
        /// <summary>
        /// Creates the toolbar margin for the given text view.
        /// Called once per SQL editor text view by the VS editor infrastructure.
        /// </summary>
        public IWpfTextViewMargin? CreateMargin(IWpfTextViewHost wpfTextViewHost, IWpfTextViewMargin marginContainer)
        {
            try
            {
                var textView = wpfTextViewHost.TextView;

                // Use GetOrCreateSingletonProperty to ensure one toolbar per text view
                return textView.Properties.GetOrCreateSingletonProperty(
                    () => new EditorToolbar(textView));
            }
            catch (System.Exception ex)
            {
                Log.Warning(ex, "EditorToolbarProvider: failed to create toolbar margin");
                return null;
            }
        }
    }
}
