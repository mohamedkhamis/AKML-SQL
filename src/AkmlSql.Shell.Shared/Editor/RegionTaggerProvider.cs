#nullable enable
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// MEF ITaggerProvider that creates <see cref="RegionTagger"/> instances for
    /// T-SQL content types. This enables VS/SSMS outlining (code folding) for
    /// --region / --endregion comment blocks.
    /// </summary>
    [Export(typeof(ITaggerProvider))]
    [ContentType("T-SQL")]
    [TagType(typeof(IOutliningRegionTag))]
    internal sealed class RegionTaggerProvider : ITaggerProvider
    {
        public ITagger<T>? CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            if (buffer == null)
                return null;

            return buffer.Properties.GetOrCreateSingletonProperty(
                typeof(RegionTagger),
                () => new RegionTagger(buffer)) as ITagger<T>;
        }
    }
}
