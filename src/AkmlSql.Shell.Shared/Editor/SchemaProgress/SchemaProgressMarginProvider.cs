#nullable enable
using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.SchemaProgress
{
    /// <summary>
    /// MEF listener that creates a <see cref="SchemaProgressMargin"/> adornment in the
    /// bottom-right corner of each SQL editor viewport, matching the "loading…" toast
    /// pattern used by modern IDE tools.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SchemaProgressListener : IWpfTextViewCreationListener
    {
        /// <summary>Adornment layer that hosts the notification box.</summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("AkmlSchemaProgress")]
        [Order(After = PredefinedAdornmentLayers.CurrentLineHighlighter)]
        internal AdornmentLayerDefinition SchemaProgressLayer = null!;

        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView == null) return;
            try
            {
                textView.Properties.GetOrCreateSingletonProperty(
                    typeof(SchemaProgressMargin),
                    () =>
                    {
                        var layer = textView.GetAdornmentLayer("AkmlSchemaProgress");
                        return new SchemaProgressMargin(textView, layer);
                    });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SchemaProgressListener: failed to create adornment");
            }
        }
    }
}
