#nullable enable
using System.ComponentModel.Composition;
using AkmlSql.Core.Config;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// T057: MEF export that creates the ghost text adornment layer and attaches
    /// <see cref="GhostTextAdornment"/> to each SQL text view. Checks the
    /// <see cref="AiSettings.InlineCompletion"/> config flag; if disabled, no adornment is created.
    /// Follows the pattern from <c>StickyScrollAdornmentProvider</c>.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class GhostTextAdornmentProvider : IWpfTextViewCreationListener
    {
        /// <summary>
        /// Defines the adornment layer used by the ghost text feature.
        /// The layer is placed above the text but below the caret.
        /// </summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("AkmlSqlGhostText")]
        [Order(After = PredefinedAdornmentLayers.Text, Before = PredefinedAdornmentLayers.Caret)]
        [TextViewRole(PredefinedTextViewRoles.Document)]
#pragma warning disable CS0169 // Field is never used -- required by MEF
        private AdornmentLayerDefinition? _ghostTextLayerDefinition;
#pragma warning restore CS0169

        /// <summary>
        /// Cached inline completion setting, read once at startup.
        /// </summary>
        private readonly bool _enabled;

        public GhostTextAdornmentProvider()
        {
            try
            {
                var settings = ConfigManager.Load();
                _enabled = settings.Ai.Enabled && settings.Ai.InlineCompletion;
            }
            catch
            {
                _enabled = false;
            }
        }

        /// <summary>
        /// Called when a new SQL text view is created. Creates the ghost text adornment
        /// only if inline completion is enabled in the configuration.
        /// </summary>
        public void TextViewCreated(IWpfTextView textView)
        {
            if (!_enabled)
                return;

            try
            {
                textView.Properties.GetOrCreateSingletonProperty(
                    () => new GhostTextAdornment(textView));

                Log.Debug("GhostTextAdornmentProvider: created adornment for view");
            }
            catch (System.Exception ex)
            {
                Log.Warning(ex, "GhostTextAdornmentProvider: failed to create adornment");
            }
        }
    }
}
