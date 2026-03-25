#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// MEF ITagger that scans the buffer for <c>--region NAME</c> / <c>--endregion</c>
    /// comment pairs and emits <see cref="IOutliningRegionTag"/> for each matched pair.
    /// This enables code folding for named regions in T-SQL editors.
    /// </summary>
    internal sealed class RegionTagger : ITagger<IOutliningRegionTag>, IDisposable
    {
        private readonly ITextBuffer _buffer;
        private readonly object _updateLock = new object();
        private ITextSnapshot _cachedSnapshot;
        private List<Region> _regions = new();

        private static readonly Regex RegionStartPattern = new(
            @"^\s*--\s*region\s+(.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        private static readonly Regex RegionEndPattern = new(
            @"^\s*--\s*endregion",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        internal RegionTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _cachedSnapshot = buffer.CurrentSnapshot;
            _regions = ParseRegions(_cachedSnapshot);
            _buffer.Changed += OnBufferChanged;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            var snapshot = _buffer.CurrentSnapshot;

            lock (_updateLock)
            {
                if (snapshot == _cachedSnapshot) return;

                _cachedSnapshot = snapshot;
                _regions = ParseRegions(snapshot);
            }

            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        public IEnumerable<ITagSpan<IOutliningRegionTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
                yield break;

            var snapshot = spans[0].Snapshot;
            List<Region> regions;
            lock (_updateLock)
            {
                regions = _regions;
            }

            foreach (var region in regions)
            {
                // Guard against stale offsets
                if (region.StartOffset >= snapshot.Length || region.EndOffset > snapshot.Length)
                    continue;
                if (region.StartOffset >= region.EndOffset)
                    continue;

                var regionSpan = new SnapshotSpan(snapshot, region.StartOffset,
                    region.EndOffset - region.StartOffset);

                // Only emit tags that overlap the requested spans
                bool overlaps = false;
                foreach (var s in spans)
                {
                    if (regionSpan.OverlapsWith(s))
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps) continue;

                yield return new TagSpan<IOutliningRegionTag>(
                    regionSpan,
                    new OutliningRegionTag(
                        isDefaultCollapsed: false,
                        isImplementation: false,
                        collapsedForm: region.Name,
                        collapsedHintForm: snapshot.GetText(regionSpan)));
            }
        }

        /// <summary>
        /// Scans the snapshot text for --region / --endregion pairs.
        /// Uses a stack to handle nesting.
        /// </summary>
        private static List<Region> ParseRegions(ITextSnapshot snapshot)
        {
            var result = new List<Region>();
            var text = snapshot.GetText();
            var stack = new Stack<(string name, int startOffset)>();

            var startMatches = RegionStartPattern.Matches(text);
            var endMatches = RegionEndPattern.Matches(text);

            // Merge all region start/end matches and process in order
            var events = new List<(int offset, bool isStart, string name)>();
            foreach (Match m in startMatches)
            {
                events.Add((m.Index, true, m.Groups[1].Value.TrimEnd('\r')));
            }
            foreach (Match m in endMatches)
            {
                events.Add((m.Index, false, string.Empty));
            }
            events.Sort((a, b) => a.offset.CompareTo(b.offset));

            foreach (var (offset, isStart, name) in events)
            {
                if (isStart)
                {
                    stack.Push((name, offset));
                }
                else if (stack.Count > 0)
                {
                    var (regionName, startOffset) = stack.Pop();
                    // End offset includes the full --endregion line
                    var lineEnd = text.IndexOf('\n', offset);
                    var endOffset = lineEnd >= 0 ? lineEnd : text.Length;

                    result.Add(new Region
                    {
                        Name = regionName,
                        StartOffset = startOffset,
                        EndOffset = endOffset
                    });
                }
            }

            return result;
        }

        public void Dispose()
        {
            _buffer.Changed -= OnBufferChanged;
        }

        private sealed class Region
        {
            public string Name { get; set; } = string.Empty;
            public int StartOffset { get; set; }
            public int EndOffset { get; set; }
        }
    }
}
