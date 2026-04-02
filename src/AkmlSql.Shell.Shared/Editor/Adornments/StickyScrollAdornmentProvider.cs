#nullable enable
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Adornments
{
    /// <summary>
    /// T095: MEF export that creates the sticky scroll adornment layer and attaches
    /// <see cref="StickyScrollAdornment"/> to each SQL text view.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class StickyScrollAdornmentProvider : IWpfTextViewCreationListener
    {
        /// <summary>
        /// Defines the adornment layer used by the sticky scroll feature.
        /// The layer is placed above the text but below the selection layer.
        /// </summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("AkmlSqlStickyScroll")]
        [Order(After = PredefinedAdornmentLayers.Text, Before = PredefinedAdornmentLayers.Selection)]
        [TextViewRole(PredefinedTextViewRoles.Document)]
#pragma warning disable CS0169 // Field is never used — required by MEF
        private AdornmentLayerDefinition? _stickyScrollLayerDefinition;
#pragma warning restore CS0169

        /// <summary>
        /// Called when a new SQL text view is created. Creates the sticky scroll adornment.
        /// </summary>
        public void TextViewCreated(IWpfTextView textView)
        {
            try
            {
                // The adornment will subscribe to layout events itself
                textView.Properties.GetOrCreateSingletonProperty(
                    () => new StickyScrollAdornment(textView));

                Log.Debug("StickyScrollAdornmentProvider: created adornment for view");
            }
            catch (System.Exception ex)
            {
                Log.Warning(ex, "StickyScrollAdornmentProvider: failed to create adornment");
            }
        }
    }
}
