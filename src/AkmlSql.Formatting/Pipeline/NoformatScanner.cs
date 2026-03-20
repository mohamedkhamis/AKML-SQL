using System.Text.RegularExpressions;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Represents a region of text that should not be formatted,
/// delimited by noformat/endnoformat tags.
/// </summary>
public class NoformatRegion
{
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public bool HasClosingTag { get; set; }
}

/// <summary>
/// Pre-scans raw SQL text for noformat region markers before parsing.
/// Supports line comments (--noformat / --endnoformat) and
/// block comments (/* noformat */ / /* endnoformat */).
/// Unmatched open tags extend to EOF. Nested opens merge into a single region.
/// Returns sorted, non-overlapping regions.
/// </summary>
public class NoformatScanner
{
    // Matches: --noformat (with optional surrounding whitespace in the comment)
    // or /* noformat */ (with optional whitespace inside)
    // Timeout guards against catastrophic backtracking on malformed input.
    private static readonly Regex OpenPattern = new(
        @"-- *noformat\b|/\* *noformat *\*/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    // Matches: --endnoformat or /* endnoformat */
    private static readonly Regex ClosePattern = new(
        @"-- *endnoformat\b|/\* *endnoformat *\*/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    public List<NoformatRegion> Scan(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        // Collect all open and close markers with their positions
        var events = new List<(int Offset, int Length, bool IsOpen)>();

        try
        {
            foreach (Match m in OpenPattern.Matches(text))
                events.Add((m.Index, m.Length, true));

            foreach (Match m in ClosePattern.Matches(text))
                events.Add((m.Index, m.Length, false));
        }
        catch (RegexMatchTimeoutException)
        {
            // Regex timed out on malformed input — return no noformat regions
            // (conservative: format everything rather than skip everything)
            return [];
        }

        // Sort by offset
        events.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var regions = new List<NoformatRegion>();
        int openStart = -1;
        int depth = 0;

        foreach (var (offset, length, isOpen) in events)
        {
            if (isOpen)
            {
                if (depth == 0)
                {
                    openStart = offset;
                }
                depth++;
            }
            else
            {
                if (depth > 0)
                {
                    depth--;
                    if (depth == 0 && openStart >= 0)
                    {
                        regions.Add(new NoformatRegion
                        {
                            StartOffset = openStart,
                            EndOffset = offset + length,
                            HasClosingTag = true
                        });
                        openStart = -1;
                    }
                }
                // Unmatched close tags are ignored
            }
        }

        // Unmatched open tag extends to EOF
        if (depth > 0 && openStart >= 0)
        {
            regions.Add(new NoformatRegion
            {
                StartOffset = openStart,
                EndOffset = text.Length,
                HasClosingTag = false
            });
        }

        // Merge overlapping regions (shouldn't happen with correct nesting, but be safe)
        return MergeRegions(regions);
    }

    /// <summary>
    /// Returns true if the given offset falls inside any noformat region.
    /// </summary>
    public static bool IsInNoformatRegion(List<NoformatRegion> regions, int offset)
    {
        // Binary search could be used for large lists, but linear is fine for typical usage
        foreach (var r in regions)
        {
            if (offset >= r.StartOffset && offset < r.EndOffset)
                return true;
            if (r.StartOffset > offset)
                break; // Regions are sorted
        }
        return false;
    }

    private static List<NoformatRegion> MergeRegions(List<NoformatRegion> regions)
    {
        if (regions.Count <= 1)
            return regions;

        regions.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));

        var merged = new List<NoformatRegion> { regions[0] };

        for (int i = 1; i < regions.Count; i++)
        {
            var last = merged[^1];
            var current = regions[i];

            if (current.StartOffset <= last.EndOffset)
            {
                // Overlapping or adjacent — merge
                last.EndOffset = Math.Max(last.EndOffset, current.EndOffset);
                last.HasClosingTag = last.HasClosingTag || current.HasClosingTag;
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }
}
