using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Web.Services;

/// <summary>
/// Pure helper that turns SQL preview text into a list of contiguous segments classified for syntax
/// coloring (keyword / string / comment / default) and flagged for search-term highlighting. Ported
/// from the desktop <c>SqlPreviewTokenizer</c> + RenderPreview clip logic. The concatenated segment
/// text always equals the input verbatim, so the page can render it safely (Blazor escapes the text).
/// </summary>
public static class HistoryPreviewHighlighter
{
    // Mirror the shared Core kind constants so existing web consumers/tests keep their names while
    // the classification itself lives in AkmlSql.Core.Text.SqlPreviewTokenizer (const-from-const).
    public const string KindKeyword = AkmlSql.Core.Text.SqlPreviewTokenizer.KindKeyword;
    public const string KindString = AkmlSql.Core.Text.SqlPreviewTokenizer.KindString;
    public const string KindComment = AkmlSql.Core.Text.SqlPreviewTokenizer.KindComment;
    public const string KindDefault = AkmlSql.Core.Text.SqlPreviewTokenizer.KindDefault;

    public readonly record struct Segment(string Text, string Kind, bool Hit);

    /// <summary>Tokenizes SQL into contiguous spans covering every character. Delegates to the shared
    /// Core tokenizer; kept here so <see cref="BuildSegments"/> and existing tests have a stable entry.</summary>
    public static IReadOnlyList<(int Start, int Length, string Kind)> Tokenize(string text) =>
        AkmlSql.Core.Text.SqlPreviewTokenizer.Tokenize(text);

    /// <summary>Extracts plain highlight terms from the search box text. Delegates to the shared
    /// <see cref="AkmlSql.Core.Text.HistorySearchTerms.Extract"/> (the canonical, quote-aware
    /// implementation); kept as a stable public entry for existing web consumers and tests.</summary>
    public static IReadOnlyList<string> ExtractTerms(string? search) =>
        AkmlSql.Core.Text.HistorySearchTerms.Extract(search);

    /// <summary>Builds render segments: syntax-classified spans, each split where a case-insensitive
    /// search-term match begins/ends so matched sub-spans carry <see cref="Segment.Hit"/>.</summary>
    public static IReadOnlyList<Segment> BuildSegments(string sql, string? search)
    {
        sql ??= string.Empty;
        var tokens = Tokenize(sql);
        var hits = FindHitRanges(sql, ExtractTerms(search));
        var segments = new List<Segment>();

        // hits are sorted by start and merged (non-overlapping, ascending); tokens are likewise emitted
        // in ascending order. So a single monotonic index walks both in one pass instead of rescanning
        // all hits per character position. A hit that spans a token boundary keeps end > cursor when the
        // next token starts, so it is naturally reprocessed without a special case.
        int hitIdx = 0;
        foreach (var (start, length, kind) in tokens)
        {
            int spanEnd = start + length, cursor = start;
            while (cursor < spanEnd)
            {
                while (hitIdx < hits.Count && hits[hitIdx].end <= cursor) hitIdx++; // drop fully-consumed hits
                if (hitIdx >= hits.Count || hits[hitIdx].start >= spanEnd)
                {
                    segments.Add(new Segment(sql.Substring(cursor, spanEnd - cursor), kind, false));
                    break;
                }
                var next = hits[hitIdx];
                int hStart = Math.Max(next.start, cursor), hEnd = Math.Min(next.end, spanEnd);
                if (hStart > cursor) segments.Add(new Segment(sql.Substring(cursor, hStart - cursor), kind, false));
                if (hEnd > hStart) segments.Add(new Segment(sql.Substring(hStart, hEnd - hStart), kind, true));
                cursor = hEnd;
            }
        }
        return segments;
    }

    private static List<(int start, int end)> FindHitRanges(string sql, IReadOnlyList<string> terms)
    {
        var ranges = new List<(int, int)>();
        foreach (var term in terms)
        {
            if (string.IsNullOrEmpty(term)) continue;
            int pos = 0;
            while (pos < sql.Length)
            {
                int idx = sql.IndexOf(term, pos, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                ranges.Add((idx, idx + term.Length));
                pos = idx + 1;
            }
        }
        if (ranges.Count == 0) return ranges;
        ranges.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : b.Item2.CompareTo(a.Item2));
        var merged = new List<(int, int)> { ranges[0] };
        for (int i = 1; i < ranges.Count; i++)
        {
            var (ls, le) = merged[^1];
            var (cs, ce) = ranges[i];
            if (cs <= le) merged[^1] = (ls, Math.Max(le, ce));
            else merged.Add((cs, ce));
        }
        return merged;
    }
}
