#nullable enable
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor.Adornments
{
    /// <summary>
    /// T097: MEF export that creates the minimap adornment layer and attaches
    /// <see cref="MinimapAdornment"/> to each SQL text view.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class MinimapAdornmentProvider : IWpfTextViewCreationListener
    {
        /// <summary>
        /// Defines the adornment layer used by the minimap feature.
        /// The layer is placed above the selection layer so the minimap is always visible.
        /// </summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("AkmlSqlMinimap")]
        [Order(After = PredefinedAdornmentLayers.Selection)]
        [TextViewRole(PredefinedTextViewRoles.Document)]
#pragma warning disable CS0169 // Field is never used — required by MEF
        private AdornmentLayerDefinition? _minimapLayerDefinition;
#pragma warning restore CS0169

        /// <summary>
        /// Called when a new SQL text view is created. Creates the minimap adornment.
        /// </summary>
        public void TextViewCreated(IWpfTextView textView)
        {
            try
            {
                textView.Properties.GetOrCreateSingletonProperty(
                    () => new MinimapAdornment(textView));

                Log.Debug("MinimapAdornmentProvider: created adornment for view");
            }
            catch (System.Exception ex)
            {
                Log.Warning(ex, "MinimapAdornmentProvider: failed to create adornment");
            }
        }
    }
}
