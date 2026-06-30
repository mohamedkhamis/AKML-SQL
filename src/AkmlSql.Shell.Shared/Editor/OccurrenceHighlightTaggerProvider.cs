#nullable enable
using System.ComponentModel.Composition;
using AkmlSql.Core.Config;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// MEF IViewTaggerProvider that creates <see cref="OccurrenceHighlightTagger"/> instances
    /// for T-SQL editor views. Guarded by <see cref="EditorProductivitySettings.HighlightOccurrences"/>.
    /// </summary>
    [Export(typeof(IViewTaggerProvider))]
    // SSMS 22 query editor reports content type "SQL"; register all three so occurrence highlight fires in SSMS.
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [TagType(typeof(ITextMarkerTag))]
    internal sealed class OccurrenceHighlightTaggerProvider : IViewTaggerProvider
    {
        private readonly bool _enabled;

        public OccurrenceHighlightTaggerProvider()
        {
            try { _enabled = ConfigManager.Load().EditorProductivity.HighlightOccurrences; }
            catch { _enabled = true; } // default to enabled on error
        }

        public ITagger<T>? CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || buffer == null)
                return null;

            // Only provide tags for the top-level buffer
            if (textView.TextBuffer != buffer)
                return null;

            if (!_enabled)
                return null;

            return buffer.Properties.GetOrCreateSingletonProperty(
                typeof(OccurrenceHighlightTagger),
                () => new OccurrenceHighlightTagger(textView, buffer)) as ITagger<T>;
        }
    }
}
