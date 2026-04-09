#nullable enable
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.SchemaProgress
{
    /// <summary>
    /// MEF export that creates a <see cref="SchemaProgressMargin"/> at the TOP of
    /// each SQL editor, positioned just below the existing <c>EditorToolbar</c>.
    /// This matches SQL Prompt's layout where the loading indicator sits above
    /// the document text rather than at the bottom.
    ///
    /// <list type="bullet">
    /// <item><c>MarginContainer(Top)</c> — same container as the EditorToolbar.</item>
    /// <item><c>Order(After = "AkmlSqlEditorToolbar")</c> — render below the toolbar.</item>
    /// <item><c>Order(Before = PredefinedMarginNames.Top)</c> — but still above the
    /// document area.</item>
    /// </list>
    /// </summary>
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name("AkmlSqlSchemaProgressMargin")]
    [Order(After = "AkmlSqlEditorToolbar")]
    [Order(Before = PredefinedMarginNames.Top)]
    [MarginContainer(PredefinedMarginNames.Top)]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SchemaProgressMarginProvider : IWpfTextViewMarginProvider
    {
        public IWpfTextViewMargin? CreateMargin(IWpfTextViewHost wpfTextViewHost, IWpfTextViewMargin marginContainer)
        {
            try
            {
                var textView = wpfTextViewHost.TextView;
                return textView.Properties.GetOrCreateSingletonProperty(
                    () => new SchemaProgressMargin(textView));
            }
            catch (System.Exception ex)
            {
                Log.Warning(ex, "SchemaProgressMarginProvider: failed to create margin");
                return null;
            }
        }
    }
}
